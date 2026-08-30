using System;
using System.Collections.Generic;

namespace WadForge.Imaging;

public static class BuiltInPalettes
{
    private static readonly IReadOnlyList<Rgb24> QuakeColors =
        CreateQuake();

    public static IReadOnlyList<Rgb24> Quake =>
        QuakeColors;

    private static IReadOnlyList<Rgb24> CreateQuake()
    {
        byte[] data =
            Convert.FromBase64String(
            "AAAADw8PHx8fLy8vPz8/S0tLW1tba2tre3t7i4uLm5ubq6uru7u7y8vL29vb6+vrDwsHFw8LHxcLJxsPLyMTNysXPy8XSzcbUzsb" +
            "W0MfY0sfa1Mfc1cfe18jg2cjj28jCwsPExMbGxsnJyczLy8/NzdLPz9XR0dnT09zW1t/Y2OLa2uXc3Oje3uvg4O7i4vLAAAABwcA" +
            "CwsAExMAGxsAIyMAKysHLy8HNzcHPz8HR0cHS0sLU1MLW1sLY2MLa2sPBwAADwAAFwAAHwAAJwAALwAANwAAPwAARwAATwAAVwAA" +
            "XwAAZwAAbwAAdwAAfwAAExMAGxsAIyMALysANy8AQzcASzsHV0MHX0cHa0sLd1MPg1cTi1sTl18bo2Mfr2cjIxMHLxcLOx8PSyMT" +
            "VysXYy8fczcjfzsrj0Mzn08zr2Mvv3cvz48r36sn78sf//MbCwcAGxMAKyMPNysTRzMbUzcjYz8rb0czf1M/i19Hm2tTp3tft4dr" +
            "w5N706OL47OXq4ujn3+Xk3OHi2d7f1tvd1Nja0tXXz9LVzdDSy83QycvNx8jKxcbIxMTFwsLDwcHu3Ofr2uPo1+Dl1d3i09rf0tf" +
            "c0NTaztLXzM/Uys3RyMrOx8jLxcbIxMTFwsLDwcH28O7y7Onv6Obr5eLo4d7l3tvh29fe2NTa1dHX0s7Uz8zQzMnNysfJx8XGxMP" +
            "DwsHb4N7Z3tvX3NnV2tfT2NXR1tPP1NHN0s/L0M3KzsvIzMnHysfFyMXDxsTCxMLBwsH//Mb798X28sTy7cPu6cPq5cLm4MHi3MH" +
            "e2MHa1MAW0cASzcAOysAKx8AGw8ACwcAAAD/CwvvExPfGxvPIyO/KyuvLy+fLy+PLy9/Ly9vLy9fKytPIyM/GxsvExMfCwsPKwAA" +
            "OwAASwcAXwcAbw8AfxcHkx8HoycLtzMPw0sbz2Mr238745dP56tf779399OLp3s7t5s3x8M35+NXf7//q+f/1///ZwAAiwAAswAA" +
            "1wAA/wAA//OT//fH////n1tT");

        if (data.Length !=
            PaletteFile.RawPaletteLength)
        {
            throw new InvalidOperationException(
                "The built-in Quake palette is invalid.");
        }

        Rgb24[] colors =
            new Rgb24[
                PaletteFile.PaletteColorCount];

        for (int index = 0;
             index <
                 colors.Length;
             index++)
        {
            int offset =
                index *
                3;

            colors[index] =
                new Rgb24(
                    data[offset],
                    data[offset + 1],
                    data[offset + 2]);
        }

        return Array.AsReadOnly(
            colors);
    }
}