using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace RevitIfcNorthPatcher
{
    internal class Program
    {
        private const string INVITATION = "**************************************************************************************************\n" +
                                          "* Revit IFC true north direction patcher (up to Revit v. 2021.1.1, Export IFC v. 21.2.0.0)       *\n" +
                                          "**************************************************************************************************\n" +
                                          "* Master-IFC should be exported with [Survey Point] or [Project Base Point] as coordinate base,  *\n" +
                                          "* Slave-IFC should be exported with [Shared Coordinates] as coordinate base.                     *\n" +
                                          "**************************************************************************************************\n";

        private static void Main()
        {
            try
            {
                Process();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            finally
            {
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadLine();
            }
        }

        private static void Process()
        {
            Console.WriteLine(INVITATION);

            Console.WriteLine("Please enter Master-IFC file path:");
            var masterIfcPath = Console.ReadLine();
            var masterIfc = ParseIfc(masterIfcPath);
            Console.WriteLine($"Master-IFC project direction vector [{masterIfc.GeometricRepresentationContext.DirectionId}]: {masterIfc.GeometricRepresentationContext.Direction.X};{masterIfc.GeometricRepresentationContext.Direction.Y}");
            Console.WriteLine($"Master-IFC project true north direction: {masterIfc.GeometricRepresentationContext.TrueNorthDirection}°");

            Console.WriteLine();

            Console.WriteLine("Please enter Slave-IFC file path:");
            var slaveIfcPath = Console.ReadLine();
            var slaveIfc = ParseIfc(slaveIfcPath);
            Console.WriteLine($"Slave-IFC site direction vector [{slaveIfc.IfcSite.DirectionId}]: {slaveIfc.IfcSite.Direction.X};{slaveIfc.IfcSite.Direction.Y}");
            Console.WriteLine($"Slave-IFC site Cartesian point [{slaveIfc.IfcSite.CartesianPointId}]: {slaveIfc.IfcSite.CartesianPoint.X};{slaveIfc.IfcSite.CartesianPoint.Y}");
            Console.WriteLine($"Slave-IFC site true north direction : {MathHelper.GetAngleInDegFromVector(slaveIfc.IfcSite.Direction)}°");

            Console.WriteLine();

            Console.WriteLine("Patch Slave-IFC file? (y/N)");
            var result = Console.ReadLine();

            if (result?.Equals("y") == true)
            {
                slaveIfc.IfcSite.RotateByAngleInDeg(masterIfc.GeometricRepresentationContext.TrueNorthDirection);
                slaveIfc.WritePatchedIfcToFile();
                Console.WriteLine("Slave-IFC file successfully patched");
            }
        }

        private static Ifc ParseIfc(string path)
        {
            var ifcIdToValue = new Dictionary<string,string>();
            var ifcIdToLineNo = new Dictionary<string, int>();
            string ifcSiteId = null;
            string ifcGeometricRepresentationContextId = null;

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (!line.StartsWith("#"))
                    continue;

                var parts = line.Split('=');
                ifcIdToValue.Add(parts[0], line);
                ifcIdToLineNo.Add(parts[0], i);

                if (ifcSiteId == null && parts[1].Contains("IFCSITE"))
                    ifcSiteId = parts[0];
                if (ifcGeometricRepresentationContextId == null && parts[1].Contains("IFCGEOMETRICREPRESENTATIONCONTEXT"))
                    ifcGeometricRepresentationContextId = parts[0];
            }

            return new Ifc(path, new ReadOnlyDictionary<string, string>(ifcIdToValue), new ReadOnlyDictionary<string, int>(ifcIdToLineNo), ifcSiteId, ifcGeometricRepresentationContextId);
        }
    }

    internal class Ifc
    {
        public string Path { get; }
        public IfcSite IfcSite { get; }
        public IfcGeometricRepresentationContext GeometricRepresentationContext { get; }

        public Ifc(string path, IReadOnlyDictionary<string, string> ifcIdToValue, IReadOnlyDictionary<string, int> ifcIdToLineNo, string ifcSiteId, string geometricRepresentationContextId)
        {
            Path = path;
            IfcSite = new IfcSite(ifcIdToValue, ifcIdToLineNo, ifcSiteId);
            GeometricRepresentationContext = new IfcGeometricRepresentationContext(ifcIdToValue, ifcIdToLineNo, geometricRepresentationContextId);
        }

        public void WritePatchedIfcToFile()
        {
            var lines = File.ReadAllLines(Path);
            lines[IfcSite.DirectionLineNo] = IfcSite.DirectionRawString;
            lines[IfcSite.CartesianPointLineNo] = IfcSite.CartesianPointRawString;
            File.WriteAllLines(Path, lines);
        }
    }

    internal class IfcSite
    {
        public string Id { get; }
        public string RawString { get; }
        public int LineNo { get; }

        public string DirectionId { get; }
        public int DirectionLineNo { get; }
        public string DirectionRawString { get; private set; }
        public Vector Direction { get; private set; }

        public string CartesianPointId { get; }
        public int CartesianPointLineNo { get; }
        public string CartesianPointRawString { get; private set; }
        public Point CartesianPoint { get; private set; }

        public IfcSite(IReadOnlyDictionary<string, string> ifcIdToValue, IReadOnlyDictionary<string, int> ifcIdToLineNo, string ifcSiteId)
        {
            try
            {
                var ifcSiteLineNo = ifcIdToLineNo[ifcSiteId];
                var ifcSiteRawString = ifcIdToValue[ifcSiteId];
                var ifcSiteValue = Regex.Match(ifcSiteRawString, Constants.SINGLE_BRACKET_PATTERN).Groups[1].Value;
                var ifcLocalPlacementId = ifcSiteValue.Split(',')[5];
                var ifcLocalPlacementRawString = ifcIdToValue[ifcLocalPlacementId];
                var ifcLocalPlacementValue = Regex.Match(ifcLocalPlacementRawString, Constants.SINGLE_BRACKET_PATTERN).Groups[1].Value;
                var ifcAxis2Placement3DId = ifcLocalPlacementValue.Split(',')[1];
                var ifcAxis2PlacementRawString = ifcIdToValue[ifcAxis2Placement3DId];
                var ifcAxis2Placement3DIdValue = Regex.Match(ifcAxis2PlacementRawString, Constants.SINGLE_BRACKET_PATTERN).Groups[1].Value;
                var ifcAxis2Placement3DIdValueParts = ifcAxis2Placement3DIdValue.Split(',');
                var ifcCartesianPointId = ifcAxis2Placement3DIdValueParts[0];
                var ifcDirectionId = ifcAxis2Placement3DIdValueParts[2];

                var ifcDirectionLineNo = ifcIdToLineNo[ifcDirectionId];
                var ifcDirectionRawString = ifcIdToValue[ifcDirectionId];
                var ifcDirectionValue = Regex.Match(ifcDirectionRawString, Constants.DOUBLE_BRACKET_PATTERN).Groups[1].Value;
                var ifcDirectionValueParts = ifcDirectionValue.Split(',');
                double.TryParse(ifcDirectionValueParts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var vectorX);
                double.TryParse(ifcDirectionValueParts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var vectorY);

                var ifcCartesianPointLineNo = ifcIdToLineNo[ifcCartesianPointId];
                var ifcCartesianPointRawString = ifcIdToValue[ifcCartesianPointId];
                var ifcCartesianPointValue = Regex.Match(ifcCartesianPointRawString, Constants.DOUBLE_BRACKET_PATTERN).Groups[1].Value;
                var ifcCartesianPointValueParts = ifcCartesianPointValue.Split(',');
                double.TryParse(ifcCartesianPointValueParts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var pointX);
                double.TryParse(ifcCartesianPointValueParts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var pointY);

                Id = ifcSiteId;
                RawString = ifcSiteRawString;
                LineNo = ifcSiteLineNo;

                DirectionId = ifcDirectionId;
                DirectionLineNo = ifcDirectionLineNo;
                DirectionRawString = ifcDirectionRawString;
                Direction = new Vector(vectorX, vectorY);

                CartesianPointId = ifcCartesianPointId;
                CartesianPointLineNo = ifcCartesianPointLineNo;
                CartesianPointRawString = ifcCartesianPointRawString;
                CartesianPoint = new Point(pointX, pointY);
            }
            catch
            {
                // ignored
            }
        }

        public void RotateByAngleInDeg(double angle)
        {
            var rotation = MathHelper.GetAngleInDegFromVector(Direction);
            rotation -= angle;
            var vector = MathHelper.GetVectorFromAngleInDeg(rotation);
            Direction = vector;
            DirectionRawString = Regex.Replace(DirectionRawString, Constants.DOUBLE_BRACKET_PATTERN, $"(({Direction.Y.ToString(CultureInfo.InvariantCulture)},{Direction.X.ToString(CultureInfo.InvariantCulture)},0.))");
            var point = MathHelper.RotatePointByAngleInDeg(CartesianPoint, angle);
            CartesianPoint = point;
            CartesianPointRawString = Regex.Replace(CartesianPointRawString, Constants.DOUBLE_BRACKET_PATTERN, $"(({CartesianPoint.X.ToString(CultureInfo.InvariantCulture)},{CartesianPoint.Y.ToString(CultureInfo.InvariantCulture)},0.))");
        }
    }

    internal class IfcGeometricRepresentationContext
    {
        public string Id { get; }
        public int LineNo { get; }
        public string RawString { get; }

        public int DirectionLineNumber { get; }
        public string DirectionId { get; }
        public Vector Direction { get; }
        public string DirectionRawString { get; }
        public double TrueNorthDirection { get; }

        public IfcGeometricRepresentationContext(IReadOnlyDictionary<string, string> ifcIdToValue, IReadOnlyDictionary<string, int> ifcIdToLineNo, string ifcGeometricRepresentationContextId)
        {
            try
            {
                var lineNo = ifcIdToLineNo[ifcGeometricRepresentationContextId];
                var ifcGeometricRepresentationContextRawString = ifcIdToValue[ifcGeometricRepresentationContextId];
                var ifcGeometricRepresentationContextValue = Regex.Match(ifcGeometricRepresentationContextRawString, Constants.SINGLE_BRACKET_PATTERN).Groups[1].Value;
                var ifcDirectionId = ifcGeometricRepresentationContextValue.Split(',')[5];
                var ifcDirectionLineNumber = ifcIdToLineNo[ifcDirectionId];
                var ifcDirectionRawString = ifcIdToValue[ifcDirectionId];
                var ifcDirectionValue = Regex.Match(ifcDirectionRawString, Constants.DOUBLE_BRACKET_PATTERN).Groups[1].Value;
                var ifcDirectionValueParts = ifcDirectionValue.Split(',');
                double.TryParse(ifcDirectionValueParts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var x);
                double.TryParse(ifcDirectionValueParts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var y);

                Id = ifcGeometricRepresentationContextId;
                RawString = ifcGeometricRepresentationContextRawString;
                LineNo = lineNo;

                DirectionId = ifcDirectionId;
                DirectionRawString = ifcDirectionRawString;
                DirectionLineNumber = ifcDirectionLineNumber;
                Direction = new Vector(x, y);

                TrueNorthDirection = MathHelper.GetAngleInDegFromVector(Direction);
            }
            catch
            {
                // ignored
            }
        }
    }

    internal static class MathHelper
    {
        private const double DEGREES_PER_RAD = 180 / Math.PI;

        public static double GetAngleInDegFromVector(Vector vector)
        {
            var angleInRad = Math.Atan2(vector.X, vector.Y);
            var angleInDeg = angleInRad * DEGREES_PER_RAD;
            return angleInDeg;
        }

        public static Vector GetVectorFromAngleInDeg(double angle)
        {
            var angleInRad = angle / DEGREES_PER_RAD;
            var x = Math.Sin(angleInRad);
            var y = Math.Cos(angleInRad);
            return new Vector(x, y);
        }

        public static Point RotatePointByAngleInDeg(Point point, double angle)
        {
            var angleInRad = Math.Abs(angle) / DEGREES_PER_RAD;
            var s = Math.Sin(angleInRad);
            var c = Math.Cos(angleInRad);

            var x = point.X * c - point.Y * s;
            var y = point.X * s + point.Y * c;

            return new Point(x, y);
        }
    }

    internal static class Constants
    {
        public const string SINGLE_BRACKET_PATTERN = @"\((.*?)\)";
        public const string DOUBLE_BRACKET_PATTERN = @"\(\((.*?)\)\)";
    }

    internal readonly struct Point
    {
        public readonly double X;
        public readonly double Y;

        public Point(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    internal readonly struct Vector
    {
        public readonly double X;
        public readonly double Y;

        public Vector(double x, double y)
        {
            X = x;
            Y = y;
        }
    }
}
