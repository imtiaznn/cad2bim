using System.Globalization;
using System.IO;
using System.Text;

namespace Cad2Bim.Bim.Ifc {
    /// <summary>A reference to a previously added entity: renders as #n.</summary>
    public readonly record struct StepRef(int Id);

    /// <summary>A STEP enumeration value: renders as .NAME.</summary>
    public readonly record struct StepEnum(string Name);

    /// <summary>The derived-attribute marker: renders as *.</summary>
    public readonly struct StepStar { }

    /// <summary>
    /// An aggregate attribute: renders as (a,b,c). A dedicated type rather than a bare array,
    /// because a lone <c>object[]</c> handed to a params method is unpacked into separate
    /// attributes — the exact bug this guards against.
    /// </summary>
    public readonly struct StepList {
        public readonly object?[] Items;
        public StepList(params object?[] items) => Items = items;
        public static StepList Of(IEnumerable<object?> items) => new(items.ToArray());
    }

    /// <summary>
    /// A minimal ISO 10303-21 (STEP physical file) emitter. Knows nothing about IFC beyond how to
    /// spell values: entities are added in order, each returning its #id, and the whole file is
    /// written at the end. Kept schema-agnostic so it is testable as pure string generation.
    /// </summary>
    public sealed class StepWriter {
        private readonly List<string> _lines = new();

        public int Add(string type, params object?[] args) {
            int id = _lines.Count + 1;
            StringBuilder line = new();
            line.Append('#').Append(id).Append('=').Append(type).Append('(');
            for (int i = 0; i < args.Length; i++) {
                if (i > 0) line.Append(',');
                AppendValue(line, args[i]);
            }
            line.Append(");");
            _lines.Add(line.ToString());
            return id;
        }

        private static void AppendValue(StringBuilder line, object? value) {
            switch (value) {
                case null:
                    line.Append('$');
                    break;
                case StepStar:
                    line.Append('*');
                    break;
                case StepRef reference:
                    line.Append('#').Append(reference.Id);
                    break;
                case StepEnum enumeration:
                    line.Append('.').Append(enumeration.Name).Append('.');
                    break;
                case bool flag:
                    line.Append(flag ? ".T." : ".F.");
                    break;
                case string text:
                    line.Append('\'').Append(Escape(text)).Append('\'');
                    break;
                case int number:
                    line.Append(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case double number:
                    string formatted = number.ToString("R", CultureInfo.InvariantCulture);
                    line.Append(formatted);
                    // STEP reals must carry a decimal point.
                    if (!formatted.Contains('.') && !formatted.Contains('E') && !formatted.Contains('e')) {
                        line.Append('.');
                    }
                    break;
                case StepList list:
                    line.Append('(');
                    for (int i = 0; i < list.Items.Length; i++) {
                        if (i > 0) line.Append(',');
                        AppendValue(line, list.Items[i]);
                    }
                    line.Append(')');
                    break;
                default:
                    throw new ArgumentException($"StepWriter cannot format a {value.GetType().Name}.");
            }
        }

        /// <summary>Apostrophes and backslashes double; other characters pass through. Callers
        /// keep strings ASCII, so the \X2\ escapes for non-Latin text are not needed here.</summary>
        private static string Escape(string text) =>
            text.Replace("\\", "\\\\").Replace("'", "''");

        public void WriteTo(TextWriter writer, string schema, string fileName,
                            string description, string application) {
            var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

            writer.WriteLine("ISO-10303-21;");
            writer.WriteLine("HEADER;");
            writer.WriteLine($"FILE_DESCRIPTION(('{Escape(description)}'),'2;1');");
            writer.WriteLine($"FILE_NAME('{Escape(fileName)}','{timestamp}',(''),(''),'{Escape(application)}','{Escape(application)}','');");
            writer.WriteLine($"FILE_SCHEMA(('{schema}'));");
            writer.WriteLine("ENDSEC;");
            writer.WriteLine("DATA;");
            foreach (string line in _lines) writer.WriteLine(line);
            writer.WriteLine("ENDSEC;");
            writer.WriteLine("END-ISO-10303-21;");
        }
    }
}
