namespace DesktopApp.Core;

public static class OhmsLaw
{
    /// <summary>Current in amperes: I = U / R.</summary>
    public static double Current(double voltage, double resistance)
    {
        if (!double.IsFinite(voltage))
            throw new ArgumentOutOfRangeException(nameof(voltage), voltage, "Voltage must be a finite number.");
        if (!double.IsFinite(resistance) || resistance <= 0)
            throw new ArgumentOutOfRangeException(nameof(resistance), resistance, "Resistance must be a finite number greater than zero.");

        return voltage / resistance;
    }
}
