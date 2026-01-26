using System;
using System.IO;

namespace RayTracing.UI
{
    internal static class ImGuiPanel
    {
        internal readonly record struct PanelLineInfo(int PosLine, int SizeLine, string PosLineText, string SizeLineText);

        public static bool TryGetPanelLineInfo(string panelName, out PanelLineInfo info, string? iniPath = null)
        {
            info = default;
            if (string.IsNullOrWhiteSpace(panelName))
                return false;

            iniPath ??= "imgui.ini";
            if (!File.Exists(iniPath))
                return false;

            string[] lines = File.ReadAllLines(iniPath);
            string header = "[Window][" + panelName + "]";

            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.Equals(lines[i].Trim(), header, StringComparison.Ordinal))
                    continue;

                int posLine = -1;
                int sizeLine = -1;
                string posText = string.Empty;
                string sizeText = string.Empty;

                for (int j = i + 1; j < lines.Length; j++)
                {
                    string rawLine = lines[j];
                    string line = rawLine.TrimStart();
                    if (line.StartsWith("[", StringComparison.Ordinal))
                        break;

                    if (line.StartsWith("Pos=", StringComparison.Ordinal))
                    {
                        posLine = j + 1; // 1-based line numbers
                        posText = rawLine;
                    }
                    else if (line.StartsWith("Size=", StringComparison.Ordinal))
                    {
                        sizeLine = j + 1;
                        sizeText = rawLine;
                    }

                    if (posLine > 0 && sizeLine > 0)
                        break;
                }

                if (posLine > 0 && sizeLine > 0)
                {
                    info = new PanelLineInfo(posLine, sizeLine, posText, sizeText);
                    return true;
                }

                return false;
            }

            return false;
        }
    }
}