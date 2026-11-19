// Application Insights for DesktopApp, plus a "DesktopApp health" workbook.
// Deploy:  az deployment group create -g <resource-group> -f monitoring/main.bicep
// Then put the `connectionString` output into the AppInsightsConnectionString variable of the DesktopApp-Telemetry variable group.
// Every workbook tile has "Pin to dashboard", so the charts can be put on any Azure dashboard.

@description('Prefix for the resource names.')
param name string = 'desktopapp'
param location string = resourceGroup().location
@description('Days to keep telemetry.')
param retentionInDays int = 90

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${name}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: retentionInDays
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${name}-insights'
  location: location
  kind: 'other'
  properties: {
    Application_Type: 'other'
    WorkspaceResourceId: workspace.id
  }
}

func tile(title string, query string, visualization string, width string) object => {
  type: 3
  name: title
  customWidth: width
  content: {
    version: 'KqlItem/1.0'
    title: title
    query: query
    queryType: 0
    resourceType: 'microsoft.insights/components'
    visualization: visualization
    size: 0
    timeContextFromParameter: 'TimeRange'
  }
}

var workbook = {
  version: 'Notebook/1.0'
  items: [
    {
      type: 9
      name: 'parameters'
      content: {
        version: 'KqlParameterItem/1.0'
        parameters: [
          {
            id: 'b5c1e2f4-8f0e-4a51-9c38-7d1c3a1f0a01'
            name: 'TimeRange'
            type: 4
            isRequired: true
            value: { durationMs: 604800000 }
            typeSettings: {
              selectableValues: [
                { durationMs: 3600000 }
                { durationMs: 86400000 }
                { durationMs: 604800000 }
                { durationMs: 2592000000 }
              ]
            }
          }
        ]
      }
    }
    tile('Sessions per hour', '''
customEvents
| where name == "SessionStarted"
| summarize Sessions = dcount(cloud_RoleInstance) by bin(timestamp, 1h)
''', 'timechart', '50')
    tile('Crash-free sessions by version', '''
customEvents
| where name == "SessionStarted"
| join kind=leftouter (exceptions | where severityLevel == 4 | distinct cloud_RoleInstance | extend Crashed = true) on cloud_RoleInstance
| summarize Sessions = count(), Crashed = countif(Crashed == true) by Version = application_Version
| extend CrashFreePercent = round(100.0 * (Sessions - Crashed) / Sessions, 1)
| order by Version desc
''', 'table', '50')
    tile('Crashes per hour', '''
exceptions
| where severityLevel == 4
| summarize Crashes = count() by bin(timestamp, 1h), type
''', 'timechart', '50')
    tile('Top errors (4 = crash, 2 = handled)', '''
union
    (exceptions | extend Error = strcat(type, ": ", outerMessage)),
    (traces | where severityLevel == 2 | extend Error = message) // Handled errors are sent without the exception, so no file paths.
| summarize Count = count(), Sessions = dcount(cloud_RoleInstance), LastSeen = max(timestamp)
    by Severity = severityLevel, Error, Version = application_Version
| top 20 by Count
''', 'table', '50')
    tile('CPU % (share of all cores)', '''
customMetrics
| where name == "desktopapp.process.cpu.percent"
| summarize Average = avg(value), P95 = percentile(value, 95) by bin(timestamp, 15m)
''', 'timechart', '50')
    tile('Memory: working set (MB)', '''
customMetrics
| where name == "dotnet.process.memory.working_set"
| summarize Average = avg(value) / 1e6, P95 = percentile(value, 95) / 1e6 by bin(timestamp, 15m)
''', 'timechart', '50')
    tile('Startup time (ms) by version', '''
customMetrics
| where name == "desktopapp.startup.duration"
| extend ms = valueSum / valueCount
| summarize P50 = percentile(ms, 50), P95 = percentile(ms, 95), Sessions = count() by Version = application_Version
| order by Version desc
''', 'table', '50')
    tile('Sessions by version and OS', '''
customEvents
| where name == "SessionStarted"
| summarize Sessions = count() by Version = application_Version, OS = tostring(customDimensions.OsVersion)
| order by Sessions desc
''', 'table', '50')
    tile('Requested features that are not available', '''
customEvents
| where name == "FeatureRequested"
| summarize Requests = count(), Sessions = dcount(cloud_RoleInstance) by Feature = tostring(customDimensions.Detail)
| order by Requests desc
''', 'barchart', '50')
    tile('Devices placed by symbol', '''
customMetrics
| where name == "desktopapp.devices.placed"
| summarize Placed = sum(value) by Symbol = tostring(customDimensions.symbol)
| order by Placed desc
''', 'barchart', '50')
  ]
  fallbackResourceIds: [appInsights.id]
  '$schema': 'https://github.com/Microsoft/Application-Insights-Workbooks/blob/master/schema/workbook.json'
}

resource healthWorkbook 'Microsoft.Insights/workbooks@2023-06-01' = {
  name: guid(appInsights.id, 'desktopapp-health')
  location: location
  kind: 'shared'
  properties: {
    displayName: 'DesktopApp health'
    category: 'workbook'
    sourceId: appInsights.id
    serializedData: string(workbook)
  }
}

output connectionString string = appInsights.properties.ConnectionString
