using LibreHardwareMonitor.Hardware;

namespace OpenFanControl.Services;

/// <summary>
/// Walks the hardware tree and calls <see cref="IHardware.Update"/> on every node,
/// which is what forces LibreHardwareMonitor to refresh sensor values.
/// </summary>
internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);

    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware sub in hardware.SubHardware)
            sub.Accept(this);
    }

    public void VisitSensor(ISensor sensor) { }

    public void VisitParameter(IParameter parameter) { }
}
