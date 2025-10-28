using System;
using Arsenal.Rhino.Spatial;

namespace Arsenal.Grasshopper.Runtime;

/// <summary>Default implementation for shared Grasshopper services.</summary>
public sealed class ServiceRegistry : IServiceRegistry
{
    /// <summary>Gets the singleton instance.</summary>
    public static ServiceRegistry Instance { get; } = new();

    private static Func<IBoundsCalculator>? _boundsFactory;
    private static IBoundsCalculator? _boundsCalculator;

    private ServiceRegistry()
    {
    }

    /// <inheritdoc/>
    public IBoundsCalculator BoundsCalculator
    {
        get
        {
            if (_boundsCalculator is not null)
            {
                return _boundsCalculator;
            }

            if (_boundsFactory is null)
            {
                throw new InvalidOperationException("Bounds calculator factory not configured. Call Initialize() first.");
            }

            IBoundsCalculator instance = _boundsFactory();

            _boundsCalculator =
                instance ?? throw new InvalidOperationException("Bounds calculator factory returned null.");
            return instance;
        }
    }

    /// <inheritdoc/>
    public void ConfigureBoundsCalculator(Func<IBoundsCalculator> factory)
    {
        _boundsFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        _boundsCalculator = null;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _boundsCalculator = null;
        _boundsFactory = null;
    }
}
