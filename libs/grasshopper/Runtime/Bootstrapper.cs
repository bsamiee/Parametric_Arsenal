using Arsenal.Rhino.Geometry.Core;
using Arsenal.Rhino.Geometry.Curves;
using Arsenal.Rhino.Spatial;

namespace Arsenal.Grasshopper.Runtime;

/// <summary>Configures shared services when the plugin loads.</summary>
public static class Bootstrapper
{
    private static bool _initialized;

    /// <summary>Ensures initialization happens when called explicitly.</summary>
    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        // Compose dependencies using proper dependency injection
        IClassifier classifier = new Classifier();
        ICentroid centroid = new Centroid(classifier);
        ICurve curves = new CurveOperations();
        IBoundsCalculator boundsCalculator = new BoundsCalculator(curves, centroid);

        ServiceRegistry.Instance.ConfigureBoundsCalculator(() => boundsCalculator);
        _initialized = true;
    }

    /// <summary>Resets shared services. Call when unloading the assembly.</summary>
    public static void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        ServiceRegistry.Instance.Reset();
        _initialized = false;
    }
}
