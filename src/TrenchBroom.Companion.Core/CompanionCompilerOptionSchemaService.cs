using System;
using System.Collections.Generic;

namespace TrenchBroom.Companion.Core;

public enum CompanionCompilerTool { Qbsp, Vis, Light, LightGlobal }
public enum CompanionCompilerOptionValueKind { Flag, Integer, Number, Text, Threads }

public sealed record CompanionCompilerOptionDefinition(
    string Id,
    CompanionCompilerTool Tool,
    string Category,
    string Flag,
    string DisplayName,
    string Description,
    CompanionCompilerOptionValueKind ValueKind = CompanionCompilerOptionValueKind.Flag,
    string DefaultValue = "",
    bool EnabledByDefault = false,
    double? Minimum = null,
    double? Maximum = null,
    string? ExclusiveGroup = null,
    bool Available = true,
    string? AvailabilityNote = null);

public sealed record CompanionCompilerOptionSchema(
    string GameId,
    string ToolchainVersion,
    string DocumentationUrl,
    IReadOnlyList<CompanionCompilerOptionDefinition> Options,
    bool VisEnabledByDefault);

public static class CompanionCompilerOptionSchemaService
{
    public const string StableDocumentationUrl = "https://ericwa.github.io/ericw-tools/";

    private static readonly CompanionCompilerOptionSchema DuskEricw0182Rc1 =
        new(
            CompanionGameProfiles.Dusk.Id,
            CompanionEricwToolchainService.RecommendedVersion,
            StableDocumentationUrl,
            CreateOptions(),
            VisEnabledByDefault: false);

    public static CompanionCompilerOptionSchema GetRequired(string gameId, string toolchainVersion)
    {
        if (string.Equals(gameId, CompanionGameProfiles.Dusk.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(toolchainVersion, CompanionEricwToolchainService.RecommendedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return DuskEricw0182Rc1;
        }

        throw new NotSupportedException(
            $"Companion does not yet have a Compile Settings schema for game '{gameId}' and toolchain '{toolchainVersion}'.");
    }

    private static IReadOnlyList<CompanionCompilerOptionDefinition> CreateOptions()
    {
        List<CompanionCompilerOptionDefinition> o = new();

        // QBSP — stable documentation option set.
        o.Add(F("qbsp.nofill", CompanionCompilerTool.Qbsp, "Geometry", "-nofill", "Disable outside filling", "Does not perform outside filling."));
        o.Add(F("qbsp.noclip", CompanionCompilerTool.Qbsp, "Geometry", "-noclip", "Do not build clip hulls", "Skips normal collision hull generation."));
        o.Add(F("qbsp.noskip", CompanionCompilerTool.Qbsp, "Geometry", "-noskip", "Keep SKIP faces", "Prevents removal of faces using the SKIP texture."));
        o.Add(F("qbsp.onlyents", CompanionCompilerTool.Qbsp, "Special-purpose", "-onlyents", "Entities-only update", "Updates only the entity lump.", available:false, note:"Separate incremental-build workflow; not safe inside Companion's normal fresh BSP build yet."));
        o.Add(F("qbsp.verbose", CompanionCompilerTool.Qbsp, "Logging", "-verbose", "Verbose output", "Print more MAP/compiler information.", group:"qbsp.logging"));
        o.Add(F("qbsp.noverbose", CompanionCompilerTool.Qbsp, "Logging", "-noverbose", "Minimal output", "Print almost no compiler information.", group:"qbsp.logging"));
        o.Add(F("qbsp.splitspecial", CompanionCompilerTool.Qbsp, "Geometry", "-splitspecial", "Split sky/water faces", "Do not combine sky and water faces into larger faces."));
        o.Add(F("qbsp.transwater", CompanionCompilerTool.Qbsp, "Portals", "-transwater", "Transparent-water portals", "Compute portals for transparent water. This is the documented default.", group:"qbsp.water"));
        o.Add(F("qbsp.notranswater", CompanionCompilerTool.Qbsp, "Portals", "-notranswater", "Opaque-water portals", "Compute portals for opaque water.", group:"qbsp.water"));
        o.Add(F("qbsp.transsky", CompanionCompilerTool.Qbsp, "Portals", "-transsky", "Transparent-sky portals", "Compute portal information for transparent sky."));
        o.Add(F("qbsp.nooldaxis", CompanionCompilerTool.Qbsp, "Texturing", "-nooldaxis", "Alternate texture alignment", "Use the alternate texture-alignment algorithm."));
        o.Add(F("qbsp.forcegoodtree", CompanionCompilerTool.Qbsp, "Geometry", "-forcegoodtree", "Force expensive BSP tree", "Experimental: spends more time in SolidBSP and may create a more optimal tree."));
        o.Add(F("qbsp.bspleak", CompanionCompilerTool.Qbsp, "Leak diagnostics", "-bspleak", "Write BSP-editor leak portals", "Creates a .por leak file."));
        o.Add(F("qbsp.oldleak", CompanionCompilerTool.Qbsp, "Leak diagnostics", "-oldleak", "Old-style leak points", "Creates an old-style .PTS leak file."));
        o.Add(F("qbsp.leaktest", CompanionCompilerTool.Qbsp, "Leak diagnostics", "-leaktest", "Fail build on leak", "Treat a detected leak as a compile error."));
        o.Add(F("qbsp.nopercent", CompanionCompilerTool.Qbsp, "Logging", "-nopercent", "Hide percent progress", "Suppress percent-completion output."));
        o.Add(F("qbsp.bsp2", CompanionCompilerTool.Qbsp, "BSP format", "-bsp2", "BSP2", "Extended BSP2 format for DUSK's Quake/WAD2 path. Companion ignores this automatically when WAD3 assets require Half-Life BSP mode.", group:"qbsp.format"));
        o.Add(F("qbsp.2psb", CompanionCompilerTool.Qbsp, "BSP format", "-2psb", "2PSB / RMQ BSP2", "Earlier RMQ/2PSB format. DUSK compatibility is not guaranteed.", group:"qbsp.format"));
        o.Add(I("qbsp.leakdist", CompanionCompilerTool.Qbsp, "Leak diagnostics", "-leakdist", "Leak point spacing", "Space between leak-file points. Documented default: 2.", "2", 1));
        o.Add(I("qbsp.subdivide", CompanionCompilerTool.Qbsp, "Geometry", "-subdivide", "Texture subdivision", "Texture subdivision size. Documented default: 240.", "240", 1));
        o.Add(T("qbsp.wadpath", CompanionCompilerTool.Qbsp, "Managed by Companion", "-wadpath", "WAD search path", "Directory searched for WAD files.", "", available:false, note:"Companion manages this automatically from the project's wads directory."));
        o.Add(F("qbsp.oldrottex", CompanionCompilerTool.Qbsp, "Texturing", "-oldrottex", "Old rotating-brush texturing", "Use the older rotate_ brush texture-alignment method."));
        o.Add(I("qbsp.maxnodesize", CompanionCompilerTool.Qbsp, "Performance", "-maxNodeSize", "Maximum node size", "Switch to cheaper spatial subdivision at this size. Default 1024; 0 disables.", "1024", 0));
        o.Add(F("qbsp.hexen2", CompanionCompilerTool.Qbsp, "Game format", "-hexen2", "Generate Hexen II BSP", "Generate a Hexen II BSP instead of a Quake BSP.", available:false, note:"The DUSK profile must not generate Hexen II BSP files."));
        o.Add(F("qbsp.wrbrushes", CompanionCompilerTool.Qbsp, "BSPX", "-wrbrushes", "Write brush collision list", "Include a BSPX brush list for brush-based collision."));
        o.Add(F("qbsp.wrbrushesonly", CompanionCompilerTool.Qbsp, "BSPX", "-wrbrushesonly", "Brush collision only", "Equivalent to -wrbrushes with -noclip."));
        o.Add(F("qbsp.notex", CompanionCompilerTool.Qbsp, "Textures", "-notex", "Placeholder textures only", "Write placeholder textures and depend on external replacements."));
        o.Add(F("qbsp.omitdetail", CompanionCompilerTool.Qbsp, "Geometry", "-omitdetail", "Omit detail brushes", "Remove detail brushes from the compile."));
        o.Add(T("qbsp.convert", CompanionCompilerTool.Qbsp, "Special-purpose", "-convert", "Convert MAP format", "Converts MAP format instead of building a BSP.", "valve", available:false, note:"MAP conversion belongs in a separate conversion workflow, not the normal Compile action."));

        // VIS — complete stable option set, visible but disabled for DUSK.
        const string visNote = "DUSK does not run VIS. These options are kept visible for reference and for the future Quake profile.";
        o.Add(Th("vis.threads", CompanionCompilerTool.Vis, "Performance", "-threads", "Worker threads", "Explicit VIS worker-thread count.", available:false, note:visNote));
        o.Add(F("vis.fast", CompanionCompilerTool.Vis, "Quality", "-fast", "Fast VIS", "Loose PVS for quick test compiles.", available:false, note:visNote));
        o.Add(I("vis.level", CompanionCompilerTool.Vis, "Quality", "-level", "VIS test level", "Detailed VIS test level 0–4. Default: 4.", "4", 0, 4, available:false, note:visNote));
        o.Add(F("vis.v", CompanionCompilerTool.Vis, "Logging", "-v", "Verbose output", "Verbose VIS output.", group:"vis.logging", available:false, note:visNote));
        o.Add(F("vis.vv", CompanionCompilerTool.Vis, "Logging", "-vv", "Very verbose output", "Very verbose VIS output.", group:"vis.logging", available:false, note:visNote));
        o.Add(F("vis.noambientsky", CompanionCompilerTool.Vis, "Ambient sounds", "-noambientsky", "Disable SKY ambient sound", "Disable SKY ambient-sound generation.", available:false, note:visNote));
        o.Add(F("vis.noambientwater", CompanionCompilerTool.Vis, "Ambient sounds", "-noambientwater", "Disable WATER ambient sound", "Disable water ambient-sound generation.", available:false, note:visNote));
        o.Add(F("vis.noambientslime", CompanionCompilerTool.Vis, "Ambient sounds", "-noambientslime", "Disable SLIME ambient sound", "Disable slime ambient-sound generation.", available:false, note:visNote));
        o.Add(F("vis.noambientlava", CompanionCompilerTool.Vis, "Ambient sounds", "-noambientlava", "Disable LAVA ambient sound", "Disable lava ambient-sound generation.", available:false, note:visNote));
        o.Add(F("vis.noambient", CompanionCompilerTool.Vis, "Ambient sounds", "-noambient", "Disable all ambient sounds", "Disable all VIS ambient-sound generation.", available:false, note:visNote));

        // LIGHT command options.
        o.Add(Th("light.threads", CompanionCompilerTool.Light, "Performance", "-threads", "Worker threads", "Automatic leaves one logical CPU free; Custom sends exactly -threads N. This changes CPU usage and compile time, not visual quality.", enabled:true));
        o.Add(F("light.extra", CompanionCompilerTool.Light, "Sampling", "-extra", "Extra samples (2x2)", "2x2 supersampling for smoother shadows.", group:"light.samples"));
        o.Add(F("light.extra4", CompanionCompilerTool.Light, "Sampling", "-extra4", "Extra4 samples (4x4)", "4x4 supersampling for higher quality at higher compile cost.", group:"light.samples"));
        o.Add(N("light.gate", CompanionCompilerTool.Light, "Performance", "-gate", "Light gate", "Minimum brightness considered non-zero. Default: 0.001.", "0.001", 0));
        o.Add(I("light.sunsamples", CompanionCompilerTool.Light, "Sampling", "-sunsamples", "Sun samples", "Sample count for sunlight penumbra/sunlight2. Default: 100.", "100", 1));
        o.Add(I("light.surflight_subdivide", CompanionCompilerTool.Light, "Surface lights", "-surflight_subdivide", "Surface-light spacing", "Global surface-light spacing. Default 128, range 64–2048.", "128", 64, 2048));
        o.Add(F("light.lit", CompanionCompilerTool.Light, "Output", "-lit", "Force colored .lit output", "Force a .lit file in DUSK's Quake/WAD2 path. Half-Life BSP stores RGB lighting in the BSP, so Companion ignores this automatically in WAD3 mode.", enabled:true));
        o.Add(F("light.onlyents", CompanionCompilerTool.Light, "Special-purpose", "-onlyents", "Entities-only light update", "Assign switchable-light styles after QBSP -onlyents.", available:false, note:"Depends on the separate QBSP -onlyents workflow, which Companion does not run during a normal fresh build."));
        o.Add(I("light.soft", CompanionCompilerTool.Light, "Postprocessing", "-soft", "Lightmap smoothing", "Average neighboring lightmap samples. 1 = 3x3, 2 = 5x5, etc.", "1", 1));
        o.Add(F("light.dirtdebug", CompanionCompilerTool.Light, "Debug", "-dirtdebug", "AO / dirt debug", "Render dirt/AO against a fullbright background for tuning."));
        o.Add(F("light.phongdebug", CompanionCompilerTool.Light, "Debug", "-phongdebug", "Phong debug", "Write interpolated normals to lit output for phong debugging."));
        o.Add(F("light.bouncedebug", CompanionCompilerTool.Light, "Debug", "-bouncedebug", "Bounce-light debug", "Write bounced lighting only for preview/debugging."));
        o.Add(F("light.surflight_dump", CompanionCompilerTool.Light, "Debug", "-surflight_dump", "Dump generated surface lights", "Save generated surface lights to mapname-surflights.map."));
        o.Add(F("light.novisapprox", CompanionCompilerTool.Light, "Accuracy", "-novisapprox", "Disable approximate light visibility", "Avoid rare light cutoffs by disabling approximate visibility culling."));
        o.Add(F("light.addmin", CompanionCompilerTool.Light, "Experimental", "-addmin", "Additive minlight", "Experimental alternate minlight behavior."));
        o.Add(F("light.lit2", CompanionCompilerTool.Light, "Experimental output", "-lit2", "Generate .lit2", "Force .lit2 output. DUSK may ignore this auxiliary format."));
        o.Add(F("light.lux", CompanionCompilerTool.Light, "Experimental output", "-lux", "Generate .lux", "Generate deluxemapping direction data. DUSK may ignore this auxiliary format."));
        o.Add(N("light.lmscale", CompanionCompilerTool.Light, "Experimental output", "-lmscale", "Global lightmap scale", "Equivalent to the _lightmap_scale worldspawn key.", "1", 0.000001));
        o.Add(F("light.bspxlit", CompanionCompilerTool.Light, "Experimental output", "-bspxlit", "Embed RGB lighting in BSPX", "Write RGB lighting data into BSPX."));
        o.Add(F("light.bspx", CompanionCompilerTool.Light, "Experimental output", "-bspx", "Embed RGB + direction data", "Write RGB lighting and direction data into BSPX."));
        o.Add(F("light.novanilla", CompanionCompilerTool.Light, "Experimental output", "-novanilla", "Omit vanilla fallback lighting", "Omit fallback standard lighting; reduces compatibility."));

        // LIGHT worldspawn/global options documented as command-line overrides.
        o.Add(N("lg.minlight", CompanionCompilerTool.LightGlobal, "Global illumination", "-minlight", "Minimum light", "Map-wide minimum light level.", "0"));
        o.Add(T("lg.minlight_color", CompanionCompilerTool.LightGlobal, "Global illumination", "-minlight_color", "Minimum-light color", "RGB minlight color, e.g. 255 255 255.", "255 255 255"));
        o.Add(N("lg.dist", CompanionCompilerTool.LightGlobal, "Global illumination", "-dist", "Light fade distance scale", "Scale the fade distance of all lights.", "1", 0.000001));
        o.Add(N("lg.range", CompanionCompilerTool.LightGlobal, "Global illumination", "-range", "Light brightness range scale", "Scale global light brightness range.", "1", 0));
        o.Add(N("lg.sunlight", CompanionCompilerTool.LightGlobal, "Sunlight", "-sunlight", "Sunlight brightness", "Brightness of direct sky sunlight.", "300", 0));
        o.Add(N("lg.anglescale", CompanionCompilerTool.LightGlobal, "Sunlight", "-anglescale", "Angle-incidence scale", "How strongly incidence angle affects brightness.", "0.5", 0, 1));
        o.Add(T("lg.sunlight_mangle", CompanionCompilerTool.LightGlobal, "Sunlight", "-sunlight_mangle", "Sun direction", "Yaw pitch roll. Straight down is 0 -90 0.", "0 -90 0"));
        o.Add(N("lg.sunlight_penumbra", CompanionCompilerTool.LightGlobal, "Sunlight", "-sunlight_penumbra", "Sun penumbra", "Sunlight penumbra width in degrees.", "4", 0));
        o.Add(T("lg.sunlight_color", CompanionCompilerTool.LightGlobal, "Sunlight", "-sunlight_color", "Sunlight color", "RGB direct-sunlight color.", "255 255 255"));
        o.Add(N("lg.sunlight2", CompanionCompilerTool.LightGlobal, "Sunlight", "-sunlight2", "Upper-hemisphere ambient sun", "Brightness of the upper-hemisphere sunlight dome.", "300", 0));
        o.Add(T("lg.sunlight_color2", CompanionCompilerTool.LightGlobal, "Sunlight", "-sunlight_color2", "Upper-hemisphere color", "RGB color for sunlight2.", "255 255 255"));
        o.Add(N("lg.sunlight3", CompanionCompilerTool.LightGlobal, "Sunlight", "-sunlight3", "Lower-hemisphere ambient sun", "Brightness of the lower-hemisphere sunlight dome.", "300", 0));
        o.Add(T("lg.sunlight_color3", CompanionCompilerTool.LightGlobal, "Sunlight", "-sunlight_color3", "Lower-hemisphere color", "RGB color for sunlight3.", "255 255 255"));
        o.Add(F("lg.dirt", CompanionCompilerTool.LightGlobal, "Ambient occlusion", "-dirt", "Dirtmapping / ambient occlusion", "Enable global dirtmapping/AO."));
        o.Add(I("lg.sunlight_dirt", CompanionCompilerTool.LightGlobal, "Ambient occlusion", "-sunlight_dirt", "Sunlight dirt override", "1 enables dirt on sunlight; -1 disables it.", "1", -1, 1));
        o.Add(I("lg.sunlight2_dirt", CompanionCompilerTool.LightGlobal, "Ambient occlusion", "-sunlight2_dirt", "Hemisphere dirt override", "1 enables dirt on sunlight2/3; -1 disables it.", "1", -1, 1));
        o.Add(I("lg.minlight_dirt", CompanionCompilerTool.LightGlobal, "Ambient occlusion", "-minlight_dirt", "Minlight dirt override", "1 enables dirt on minlight; -1 disables it.", "1", -1, 1));
        o.Add(I("lg.dirtmode", CompanionCompilerTool.LightGlobal, "Ambient occlusion", "-dirtmode", "Dirt sampling mode", "0 = ordered; 1 = randomized.", "0", 0, 1));
        o.Add(N("lg.dirtdepth", CompanionCompilerTool.LightGlobal, "Ambient occlusion", "-dirtdepth", "Dirt depth", "Maximum AO occlusion depth. Default: 128.", "128", 0));
        o.Add(N("lg.dirtscale", CompanionCompilerTool.LightGlobal, "Ambient occlusion", "-dirtscale", "Dirt scale", "Simple AO intensity multiplier. Default: 1.", "1", 0));
        o.Add(N("lg.dirtgain", CompanionCompilerTool.LightGlobal, "Ambient occlusion", "-dirtgain", "Dirt gain", "AO exponent. Default: 1.", "1", 0));
        o.Add(N("lg.dirtangle", CompanionCompilerTool.LightGlobal, "Ambient occlusion", "-dirtangle", "Dirt cone angle", "AO cone angle in degrees; documented range 1–90.", "88", 1, 90));
        o.Add(N("lg.gamma", CompanionCompilerTool.LightGlobal, "Postprocessing", "-gamma", "Lightmap gamma", "Final lightmap brightness adjustment. Default: 1.", "1", 0.000001));
        o.Add(F("lg.bounce", CompanionCompilerTool.LightGlobal, "Bounce / radiosity", "-bounce", "Bounce lighting", "Enable radiosity/indirect bounce lighting."));
        o.Add(N("lg.bouncescale", CompanionCompilerTool.LightGlobal, "Bounce / radiosity", "-bouncescale", "Bounce brightness scale", "Scale bounced-light brightness. Default: 1.", "1", 0));
        o.Add(N("lg.bouncecolorscale", CompanionCompilerTool.LightGlobal, "Bounce / radiosity", "-bouncecolorscale", "Texture color contribution", "0 ignores texture colors; 1 fully multiplies bounce by texture color.", "0", 0, 1));
        o.Add(I("lg.bouncestyled", CompanionCompilerTool.LightGlobal, "Bounce / radiosity", "-bouncestyled", "Bounce styled lights", "1 allows switchable/flickering lights to bounce.", "1", 0, 1));
        o.Add(I("lg.spotlightautofalloff", CompanionCompilerTool.LightGlobal, "Spotlights", "-spotlightautofalloff", "Automatic spotlight falloff", "1 derives spotlight falloff from the target distance.", "1", 0, 1));

        return o;
    }

    private static CompanionCompilerOptionDefinition F(string id, CompanionCompilerTool tool, string category, string flag, string name, string desc, bool enabled=false, string? group=null, bool available=true, string? note=null) =>
        new(id, tool, category, flag, name, desc, CompanionCompilerOptionValueKind.Flag, "", enabled, null, null, group, available, note);

    private static CompanionCompilerOptionDefinition I(string id, CompanionCompilerTool tool, string category, string flag, string name, string desc, string value, double? min=null, double? max=null, bool enabled=false, string? group=null, bool available=true, string? note=null) =>
        new(id, tool, category, flag, name, desc, CompanionCompilerOptionValueKind.Integer, value, enabled, min, max, group, available, note);

    private static CompanionCompilerOptionDefinition N(string id, CompanionCompilerTool tool, string category, string flag, string name, string desc, string value, double? min=null, double? max=null, bool enabled=false, string? group=null, bool available=true, string? note=null) =>
        new(id, tool, category, flag, name, desc, CompanionCompilerOptionValueKind.Number, value, enabled, min, max, group, available, note);

    private static CompanionCompilerOptionDefinition T(string id, CompanionCompilerTool tool, string category, string flag, string name, string desc, string value, bool enabled=false, string? group=null, bool available=true, string? note=null) =>
        new(id, tool, category, flag, name, desc, CompanionCompilerOptionValueKind.Text, value, enabled, null, null, group, available, note);

    private static CompanionCompilerOptionDefinition Th(string id, CompanionCompilerTool tool, string category, string flag, string name, string desc, bool enabled=false, bool available=true, string? note=null) =>
        new(id, tool, category, flag, name, desc, CompanionCompilerOptionValueKind.Threads, CompanionBuildSettingValues.AutomaticThreads, enabled, 1, 256, null, available, note);
}
