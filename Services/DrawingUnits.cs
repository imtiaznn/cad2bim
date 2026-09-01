using ACadSharp.Types.Units;

namespace Cad2Bim.Services {
    /// <summary>
    /// Drawing-unit bookkeeping. Geometry is kept in the file's own coordinates, so every
    /// physical setting — wall thickness above all — is stated in millimetres and converted
    /// here, at the edge, using the drawing's INSUNITS.
    /// </summary>
    public static class DrawingUnits {
        // A drawing that declares no units is almost always an architectural plan drawn in
        // millimetres, so that is what an unlabelled file is read as.
        public const UnitsType Fallback = UnitsType.Millimeters;

        /// <summary>How many millimetres one drawing unit spans.</summary>
        public static double MillimetersPerUnit(UnitsType units) => units switch {
            UnitsType.Angstroms => 1e-7,
            UnitsType.Nanometers => 1e-6,
            UnitsType.Microinches => 2.54e-5,
            UnitsType.Microns => 1e-3,
            UnitsType.Mils => 0.0254,
            UnitsType.Millimeters => 1.0,
            UnitsType.Centimeters => 10.0,
            UnitsType.Inches => 25.4,
            UnitsType.USSurveyInches => 25.400050800101602,
            UnitsType.Decimeters => 100.0,
            UnitsType.Feet => 304.8,
            UnitsType.USSurveyFeet => 304.80060960121920,
            UnitsType.Yards => 914.4,
            UnitsType.USSurveyYards => 914.40182880365760,
            UnitsType.Meters => 1e3,
            UnitsType.Decameters => 1e4,
            UnitsType.Hectometers => 1e5,
            UnitsType.Kilometers => 1e6,
            UnitsType.Miles => 1609344.0,
            UnitsType.USSurveyMiles => 1609347.2186944374,
            UnitsType.Gigameters => 1e12,
            UnitsType.AstronomicalUnits => 1.495978707e14,
            UnitsType.LightYears => 9.4607304725808e18,
            UnitsType.Parsecs => 3.0856775814913673e19,
            _ => MillimetersPerUnit(Fallback) // Unitless
        };

        /// <summary>Short label for the status bar; an unlabelled drawing says what it was assumed to be.</summary>
        public static string Name(UnitsType units) => units switch {
            UnitsType.Unitless => $"unitless, read as {Name(Fallback)}",
            UnitsType.Millimeters => "mm",
            UnitsType.Centimeters => "cm",
            UnitsType.Decimeters => "dm",
            UnitsType.Meters => "m",
            UnitsType.Kilometers => "km",
            UnitsType.Inches or UnitsType.USSurveyInches => "in",
            UnitsType.Feet or UnitsType.USSurveyFeet => "ft",
            UnitsType.Yards or UnitsType.USSurveyYards => "yd",
            UnitsType.Miles or UnitsType.USSurveyMiles => "mi",
            _ => units.ToString().ToLowerInvariant()
        };
    }
}
