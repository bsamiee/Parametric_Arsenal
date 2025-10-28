using System;
using Arsenal.Rhino.Spatial;

namespace Arsenal.Grasshopper.Runtime;

/// <summary>Interface for accessing shared Grasshopper services.</summary>
public interface IServiceRegistry
{
    /// <summary>Gets the configured bounds calculator.</summary>
    IBoundsCalculator BoundsCalculator { get; }

    /// <summary>Replaces the factory used to create bounds calculators.</summary>
    void ConfigureBoundsCalculator(Func<IBoundsCalculator> factory);

    /// <summary>Resets the registry to its uninitialized state.</summary>
    void Reset();
}
