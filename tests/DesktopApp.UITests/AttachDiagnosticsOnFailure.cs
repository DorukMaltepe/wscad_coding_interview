using NUnit.Framework.Interfaces;

[assembly: DesktopApp.UITests.AttachDiagnosticsOnFailure]

namespace DesktopApp.UITests;

/// <summary>
/// After every test: if it failed, attaches the screenshots and crash logs AppSession saved, so they show up on the
/// failed test in Azure DevOps (PublishTestResults uploads TRX attachments). If it passed, deletes them.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class AttachDiagnosticsOnFailureAttribute : Attribute, ITestAction
{
    /// <summary>The current test's diagnostics folder.</summary>
    public static string Folder => Path.Combine(TestContext.CurrentContext.WorkDirectory, "diagnostics", TestContext.CurrentContext.Test.ID);

    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test) { }

    public void AfterTest(ITest test)
    {
        if (!Directory.Exists(Folder))
            return;

        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            foreach (var file in Directory.GetFiles(Folder))
                TestContext.AddTestAttachment(file);
        }
        else
        {
            Directory.Delete(Folder, recursive: true);
        }
    }
}
