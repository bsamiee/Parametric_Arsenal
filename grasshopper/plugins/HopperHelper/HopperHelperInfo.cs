using System;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace HopperHelper
{
    public class HopperHelperInfo : GH_AssemblyInfo
    {
        public override string Name => "HopperHelper";

        public override Bitmap? Icon => null;

        public override string Description => "Grasshopper plugin utilities for Parametric Arsenal.";

        public override Guid Id => new Guid("a8dff0a5-8d7d-420e-9e7c-4a68801d38f9");

        public override string AuthorName => "Bardia Samiee";

        public override string AuthorContact => "b.samiee93@gmail.com";
    }
}
