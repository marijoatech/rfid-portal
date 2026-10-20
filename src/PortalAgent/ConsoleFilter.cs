using System;
using System.IO;
using System.Text;

namespace PortalAgent
{
    /// <summary>
    /// Filtra la salida de depuracion del SDK SReader ("dll ant:X") para no
    /// ensuciar la consola. El archivo de log del agente no la contiene.
    /// </summary>
    public sealed class ConsoleFilter : TextWriter
    {
        private readonly TextWriter inner;
        private readonly StringBuilder line = new StringBuilder();

        public ConsoleFilter(TextWriter inner)
        {
            this.inner = inner;
        }

        public override Encoding Encoding
        {
            get { return inner.Encoding; }
        }

        public override void Write(char value)
        {
            if (value == '\r') { return; }
            if (value == '\n') { FlushLine(); return; }
            line.Append(value);
        }

        public override void Write(string value)
        {
            if (value == null) { return; }
            foreach (char c in value) { Write(c); }
        }

        public override void WriteLine(string value)
        {
            Write(value);
            Write('\n');
        }

        public override void WriteLine()
        {
            Write('\n');
        }

        public override void Flush()
        {
            if (line.Length > 0) { FlushLine(); }
            inner.Flush();
        }

        private void FlushLine()
        {
            string text = line.ToString();
            line.Clear();
            if (text.StartsWith("dll ant:", StringComparison.OrdinalIgnoreCase)) { return; }
            inner.WriteLine(text);
        }
    }
}
