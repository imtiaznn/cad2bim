using System.IO;

namespace Cad2Bim.Bim.Ifc {
    /// <summary>
    /// Serializes a <see cref="BimModel"/> as an IFC4 file Revit can open or link: the usual
    /// spatial tree, walls as extruded footprint profiles with an axis curve, openings cut with
    /// <c>IfcRelVoidsElement</c> and filled by doors and windows. All geometry is emitted in
    /// millimetres, in world coordinates, with identity placements — the least machinery that
    /// downstream importers agree on.
    /// </summary>
    public sealed class IfcExporter : IBimExporter {
        private const string ApplicationName = "Cad2Bim";

        public void Export(BimModel model, string path) {
            using StreamWriter writer = new(path);
            Write(model, writer, Path.GetFileName(path));
        }

        public string ExportToString(BimModel model) {
            using StringWriter writer = new();
            Write(model, writer, model.ProjectName + ".ifc");
            return writer.ToString();
        }

        private static void Write(BimModel model, TextWriter output, string fileName) {
            StepWriter step = new();

            // --- ownership ------------------------------------------------------------------
            int person = step.Add("IFCPERSON", null, null, ApplicationName, null, null, null, null, null);
            int organization = step.Add("IFCORGANIZATION", null, ApplicationName, null, null, null);
            int personAndOrg = step.Add("IFCPERSONANDORGANIZATION", Ref(person), Ref(organization), null);
            int application = step.Add("IFCAPPLICATION", Ref(organization), "1.0", ApplicationName, ApplicationName);
            int ownerHistory = step.Add("IFCOWNERHISTORY", Ref(personAndOrg), Ref(application), null,
                                        new StepEnum("ADDED"), null, null, null,
                                        (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            // --- units: millimetres ---------------------------------------------------------
            int lengthUnit = step.Add("IFCSIUNIT", new StepStar(), new StepEnum("LENGTHUNIT"),
                                      new StepEnum("MILLI"), new StepEnum("METRE"));
            int areaUnit = step.Add("IFCSIUNIT", new StepStar(), new StepEnum("AREAUNIT"),
                                    null, new StepEnum("SQUARE_METRE"));
            int volumeUnit = step.Add("IFCSIUNIT", new StepStar(), new StepEnum("VOLUMEUNIT"),
                                      null, new StepEnum("CUBIC_METRE"));
            int angleUnit = step.Add("IFCSIUNIT", new StepStar(), new StepEnum("PLANEANGLEUNIT"),
                                     null, new StepEnum("RADIAN"));
            int units = step.Add("IFCUNITASSIGNMENT",
                                 new StepList(Ref(lengthUnit), Ref(areaUnit), Ref(volumeUnit), Ref(angleUnit)));

            // --- representation contexts ----------------------------------------------------
            int worldOrigin = step.Add("IFCCARTESIANPOINT", new StepList(0.0, 0.0, 0.0));
            int worldPlacement = step.Add("IFCAXIS2PLACEMENT3D", Ref(worldOrigin), null, null);
            int modelContext = step.Add("IFCGEOMETRICREPRESENTATIONCONTEXT",
                                        null, "Model", 3, 1e-5, Ref(worldPlacement), null);
            int axisContext = step.Add("IFCGEOMETRICREPRESENTATIONSUBCONTEXT",
                                       "Axis", "Model", new StepStar(), new StepStar(), new StepStar(),
                                       new StepStar(), Ref(modelContext), null,
                                       new StepEnum("MODEL_VIEW"), null);
            int bodyContext = step.Add("IFCGEOMETRICREPRESENTATIONSUBCONTEXT",
                                       "Body", "Model", new StepStar(), new StepStar(), new StepStar(),
                                       new StepStar(), Ref(modelContext), null,
                                       new StepEnum("MODEL_VIEW"), null);

            // --- spatial tree ---------------------------------------------------------------
            int project = step.Add("IFCPROJECT", IfcGuid.New(), Ref(ownerHistory), model.ProjectName,
                                   null, null, null, null, new StepList(Ref(modelContext)), Ref(units));

            int sitePlacement = step.Add("IFCLOCALPLACEMENT", null, Ref(worldPlacement));
            int site = step.Add("IFCSITE", IfcGuid.New(), Ref(ownerHistory), "Site", null, null,
                                Ref(sitePlacement), null, null, new StepEnum("ELEMENT"),
                                null, null, null, null, null);

            int buildingPlacement = step.Add("IFCLOCALPLACEMENT", Ref(sitePlacement), Ref(worldPlacement));
            int building = step.Add("IFCBUILDING", IfcGuid.New(), Ref(ownerHistory), "Building", null, null,
                                    Ref(buildingPlacement), null, null, new StepEnum("ELEMENT"),
                                    null, null, null);

            step.Add("IFCRELAGGREGATES", IfcGuid.New(), Ref(ownerHistory), null, null,
                     Ref(project), new StepList(Ref(site)));
            step.Add("IFCRELAGGREGATES", IfcGuid.New(), Ref(ownerHistory), null, null,
                     Ref(site), new StepList(Ref(building)));

            foreach (BimStorey storey in model.Storeys) {
                WriteStorey(step, storey, ownerHistory, building, buildingPlacement, axisContext, bodyContext);
            }

            step.WriteTo(output, "IFC4", fileName,
                         "ViewDefinition [ReferenceView_V1.2]", ApplicationName);
        }

        private static void WriteStorey(StepWriter step, BimStorey storey, int ownerHistory,
                                        int building, int buildingPlacement, int axisContext, int bodyContext) {
            int storeyOrigin = step.Add("IFCCARTESIANPOINT", new StepList(0.0, 0.0, storey.ElevationMm));
            int storeyAxes = step.Add("IFCAXIS2PLACEMENT3D", Ref(storeyOrigin), null, null);
            int storeyPlacement = step.Add("IFCLOCALPLACEMENT", Ref(buildingPlacement), Ref(storeyAxes));
            int storeyEntity = step.Add("IFCBUILDINGSTOREY", IfcGuid.New(), Ref(ownerHistory), storey.Name,
                                        null, null, Ref(storeyPlacement), null, null,
                                        new StepEnum("ELEMENT"), storey.ElevationMm);

            step.Add("IFCRELAGGREGATES", IfcGuid.New(), Ref(ownerHistory), null, null,
                     Ref(building), new StepList(Ref(storeyEntity)));

            List<StepRef> contained = new();
            int doorOrdinal = 0, windowOrdinal = 0, openingOrdinal = 0;

            foreach (BimColumn column in storey.Columns) {
                contained.Add(Ref(WriteColumn(step, column, ownerHistory, storeyPlacement, bodyContext)));
            }

            foreach (BimWall wall in storey.Walls) {
                int wallEntity = WriteWall(step, wall, ownerHistory, storeyPlacement, axisContext, bodyContext);
                contained.Add(Ref(wallEntity));

                foreach (BimOpening opening in wall.Openings) {
                    openingOrdinal++;
                    int openingEntity = WriteOpening(step, opening, openingOrdinal, ownerHistory,
                                                     storeyPlacement, bodyContext);
                    step.Add("IFCRELVOIDSELEMENT", IfcGuid.New(), Ref(ownerHistory), null, null,
                             Ref(wallEntity), Ref(openingEntity));

                    int? filler = opening switch {
                        BimDoor door => WriteDoor(step, door, ++doorOrdinal, ownerHistory,
                                                  storeyPlacement, bodyContext),
                        BimWindow window => WriteWindow(step, window, ++windowOrdinal, ownerHistory,
                                                        storeyPlacement, bodyContext),
                        _ => null // Unknown stays an honest void
                    };

                    if (filler is int filled) {
                        step.Add("IFCRELFILLSELEMENT", IfcGuid.New(), Ref(ownerHistory), null, null,
                                 Ref(openingEntity), Ref(filled));
                        contained.Add(Ref(filled));
                    }
                }
            }

            if (contained.Count > 0) {
                step.Add("IFCRELCONTAINEDINSPATIALSTRUCTURE", IfcGuid.New(), Ref(ownerHistory), null, null,
                         StepList.Of(contained.Cast<object?>()), Ref(storeyEntity));
            }
        }

        private static int WriteWall(StepWriter step, BimWall wall, int ownerHistory,
                                     int storeyPlacement, int axisContext, int bodyContext) {
            int placement = IdentityPlacement(step, storeyPlacement);

            // Axis: the centreline, the curve Revit reads as the wall's location line.
            int axisLine = step.Add("IFCPOLYLINE",
                new StepList(Ref(Point2(step, wall.AxisStart)), Ref(Point2(step, wall.AxisEnd))));
            int axisShape = step.Add("IFCSHAPEREPRESENTATION", Ref(axisContext), "Axis", "Curve2D",
                                     new StepList(Ref(axisLine)));

            // Body: the analytic footprint, extruded to the wall height.
            int outerCurve = ClosedPolyline(step, wall.Footprint);
            int profile = step.Add("IFCARBITRARYCLOSEDPROFILEDEF", new StepEnum("AREA"), null, Ref(outerCurve));

            int solid = ExtrudedSolid(step, profile, baseZ: 0, depth: wall.HeightMm);
            int bodyShape = step.Add("IFCSHAPEREPRESENTATION", Ref(bodyContext), "Body", "SweptSolid",
                                     new StepList(Ref(solid)));

            int shape = step.Add("IFCPRODUCTDEFINITIONSHAPE", null, null,
                                 new StepList(Ref(axisShape), Ref(bodyShape)));

            return step.Add("IFCWALL", IfcGuid.New(), Ref(ownerHistory), wall.Name, null, null,
                            Ref(placement), Ref(shape), null, new StepEnum("STANDARD"));
        }

        private static int WriteColumn(StepWriter step, BimColumn column, int ownerHistory,
                                       int storeyPlacement, int bodyContext) {
            int placement = IdentityPlacement(step, storeyPlacement);
            int shape = BoxShape(step, bodyContext, column.Footprint.Points, 0, column.HeightMm);

            return step.Add("IFCCOLUMN", IfcGuid.New(), Ref(ownerHistory), column.Name, null, null,
                            Ref(placement), Ref(shape), null, new StepEnum("COLUMN"));
        }

        private static int WriteOpening(StepWriter step, BimOpening opening, int ordinal, int ownerHistory,
                                        int storeyPlacement, int bodyContext) {
            int placement = IdentityPlacement(step, storeyPlacement);
            int shape = BoxShape(step, bodyContext, opening.FootprintRect, opening.SillMm,
                                 opening.HeadMm - opening.SillMm);

            return step.Add("IFCOPENINGELEMENT", IfcGuid.New(), Ref(ownerHistory), $"Opening-{ordinal}",
                            null, null, Ref(placement), Ref(shape), null, new StepEnum("OPENING"));
        }

        private static int WriteDoor(StepWriter step, BimDoor door, int ordinal, int ownerHistory,
                                     int storeyPlacement, int bodyContext) {
            int placement = IdentityPlacement(step, storeyPlacement);
            int shape = BoxShape(step, bodyContext, door.FootprintRect, 0, door.HeadMm);

            // Hinge end and swing side fold into IFC's door operation types; with one leaf that
            // is a left- or right-hung single swing.
            string operation = door.HingeAtStart ^ door.SwingsPositiveNormal
                ? "SINGLE_SWING_RIGHT" : "SINGLE_SWING_LEFT";

            int doorEntity = step.Add("IFCDOOR", IfcGuid.New(), Ref(ownerHistory), $"Door-{ordinal}",
                                      null, null, Ref(placement), Ref(shape), null,
                                      door.HeadMm, door.WidthMm,
                                      new StepEnum("DOOR"), new StepEnum(operation), null);

            int doorType = step.Add("IFCDOORTYPE", IfcGuid.New(), Ref(ownerHistory),
                                    $"Door {door.WidthMm:0} x {door.HeadMm:0}", null, null, null, null, null,
                                    null, new StepEnum("DOOR"), new StepEnum(operation), false, null);
            step.Add("IFCRELDEFINESBYTYPE", IfcGuid.New(), Ref(ownerHistory), null, null,
                     new StepList(Ref(doorEntity)), Ref(doorType));

            return doorEntity;
        }

        private static int WriteWindow(StepWriter step, BimWindow window, int ordinal, int ownerHistory,
                                       int storeyPlacement, int bodyContext) {
            int placement = IdentityPlacement(step, storeyPlacement);
            int shape = BoxShape(step, bodyContext, window.FootprintRect, window.SillMm,
                                 window.HeadMm - window.SillMm);

            int windowEntity = step.Add("IFCWINDOW", IfcGuid.New(), Ref(ownerHistory), $"Window-{ordinal}",
                                        null, null, Ref(placement), Ref(shape), null,
                                        window.HeadMm - window.SillMm, window.WidthMm,
                                        new StepEnum("WINDOW"), new StepEnum("SINGLE_PANEL"), null);

            int windowType = step.Add("IFCWINDOWTYPE", IfcGuid.New(), Ref(ownerHistory),
                                      $"Window {window.WidthMm:0} x {window.HeadMm - window.SillMm:0}",
                                      null, null, null, null, null, null,
                                      new StepEnum("WINDOW"), new StepEnum("SINGLE_PANEL"), false, null);
            step.Add("IFCRELDEFINESBYTYPE", IfcGuid.New(), Ref(ownerHistory), null, null,
                     new StepList(Ref(windowEntity)), Ref(windowType));

            return windowEntity;
        }

        // --- geometry helpers ---------------------------------------------------------------

        private static StepRef Ref(int id) => new(id);

        private static int Point2(StepWriter step, BimPoint point) =>
            step.Add("IFCCARTESIANPOINT", new StepList(point.X, point.Y));

        private static int IdentityPlacement(StepWriter step, int relativeTo) {
            int origin = step.Add("IFCCARTESIANPOINT", new StepList(0.0, 0.0, 0.0));
            int axes = step.Add("IFCAXIS2PLACEMENT3D", Ref(origin), null, null);
            return step.Add("IFCLOCALPLACEMENT", Ref(relativeTo), Ref(axes));
        }

        /// <summary>A closed IfcPolyline: the ring's points with the first repeated at the end.</summary>
        private static int ClosedPolyline(StepWriter step, BimPolygon ring) {
            List<object?> points = ring.Points.Select(p => (object?)Ref(Point2(step, p))).ToList();
            points.Add(points[0]);
            return step.Add("IFCPOLYLINE", StepList.Of(points));
        }

        /// <summary>An extruded solid whose profile sits at the given Z, swept straight up.</summary>
        private static int ExtrudedSolid(StepWriter step, int profile, double baseZ, double depth) {
            int origin = step.Add("IFCCARTESIANPOINT", new StepList(0.0, 0.0, baseZ));
            int position = step.Add("IFCAXIS2PLACEMENT3D", Ref(origin), null, null);
            int up = step.Add("IFCDIRECTION", new StepList(0.0, 0.0, 1.0));
            return step.Add("IFCEXTRUDEDAREASOLID", Ref(profile), Ref(position), Ref(up), Math.Max(depth, 1.0));
        }

        /// <summary>Product shape for a plan rectangle extruded between two heights.</summary>
        private static int BoxShape(StepWriter step, int bodyContext, IReadOnlyList<BimPoint> rectangle,
                                    double baseZ, double depth) {
            int curve = ClosedPolyline(step, new BimPolygon(rectangle));
            int profile = step.Add("IFCARBITRARYCLOSEDPROFILEDEF", new StepEnum("AREA"), null, Ref(curve));
            int solid = ExtrudedSolid(step, profile, baseZ, depth);
            int representation = step.Add("IFCSHAPEREPRESENTATION", Ref(bodyContext), "Body", "SweptSolid",
                                          new StepList(Ref(solid)));
            return step.Add("IFCPRODUCTDEFINITIONSHAPE", null, null, new StepList(Ref(representation)));
        }
    }
}
