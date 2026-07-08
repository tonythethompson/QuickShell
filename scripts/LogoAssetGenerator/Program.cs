using SkiaSharp;
using Svg.Skia;

if (args.Length >= 3 && args[0] == "--posters")
{
    try
    {
        return GenerateStorePosters(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

if (args.Length >= 5 && args[0] == "--render")
{
    try
    {
        var svgPath = Path.GetFullPath(args[1]);
        var outPath = Path.GetFullPath(args[2]);
        var width = int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
        var height = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        RenderSvgToPng(svgPath, outPath, width, height);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: LogoAssetGenerator <microLogo.svg> <outputDir>");
    Console.Error.WriteLine("       LogoAssetGenerator --render <any.svg> <out.png> <width> <height>");
    Console.Error.WriteLine("       LogoAssetGenerator --posters <icon.svg|icon.png> <storeListingDir>");
    return 1;
}

var microSvgPath = Path.GetFullPath(args[0]);
var outDir = Path.GetFullPath(args[1]);
Directory.CreateDirectory(outDir);

if (!File.Exists(microSvgPath))
{
    Console.Error.WriteLine($"Missing micro logo source: {microSvgPath}");
    return 1;
}

using var svg = new SKSvg();
if (svg.Load(microSvgPath) is null || svg.Picture is null)
{
    throw new InvalidOperationException($"Failed to load SVG: {microSvgPath}");
}

var picture = svg.Picture;
var bounds = picture.CullRect;

var msixAssets = new (string Path, int Width, int Height)[]
{
    ("StoreLogo.png", 50, 50),
    ("Square44x44Logo.png", 44, 44),
    ("Square44x44Logo.targetsize-24_altform-unplated.png", 24, 24),
    ("Square44x44Logo.scale-200.png", 88, 88),
    ("LockScreenLogo.scale-200.png", 48, 48),
    ("Square150x150Logo.scale-200.png", 300, 300),
    ("Wide310x150Logo.scale-200.png", 620, 300),
    ("SplashScreen.scale-200.png", 620, 300),
};

foreach (var (path, width, height) in msixAssets)
{
    RenderSquareLogo(outDir, path, picture, bounds, width, height);
}

CopyIfExists(outDir, "Square150x150Logo.scale-200.png", "Square150x150Logo.png");
CopyIfExists(outDir, "Wide310x150Logo.scale-200.png", "Wide310x150Logo.png");
CopyIfExists(outDir, "SplashScreen.scale-200.png", "SplashScreen.png");

var storeListingDir = Path.Combine(outDir, "StoreListing");
Directory.CreateDirectory(storeListingDir);

// Partner Center → Store logos / Store display images (exact pixel dimensions).
var storeListingAssets = new (string Path, int Width, int Height, bool Poster)[]
{
    ("PosterArt_720x1080.png", 720, 1080, true),
    ("PosterArt_1440x2160.png", 1440, 2160, true),
    ("BoxArt_1080x1080.png", 1080, 1080, false),
    ("BoxArt_2160x2160.png", 2160, 2160, false),
    ("AppTile_300x300.png", 300, 300, false),
    ("AppTile_150x150.png", 150, 150, false),
    ("AppTile_71x71.png", 71, 71, false),
};

foreach (var (path, width, height, poster) in storeListingAssets)
{
    if (poster)
    {
        RenderPosterLogoFromPicture(storeListingDir, path, picture, bounds, width, height);
    }
    else
    {
        RenderSquareLogo(storeListingDir, path, picture, bounds, width, height);
    }
}

return 0;

static int GenerateStorePosters(string iconSourcePath, string storeListingDir)
{
    Directory.CreateDirectory(storeListingDir);

    if (!File.Exists(iconSourcePath))
    {
        Console.Error.WriteLine($"Missing icon source: {iconSourcePath}");
        return 1;
    }

    if (iconSourcePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
    {
        using var bitmap = SKBitmap.Decode(iconSourcePath)
            ?? throw new InvalidOperationException($"Failed to decode PNG: {iconSourcePath}");
        var bounds = new SKRect(0, 0, bitmap.Width, bitmap.Height);
        RenderPosterLogoFromBitmap(storeListingDir, "PosterArt_720x1080.png", bitmap, bounds, 720, 1080);
        RenderPosterLogoFromBitmap(storeListingDir, "PosterArt_1440x2160.png", bitmap, bounds, 1440, 2160);
        return 0;
    }

    using var svg = new SKSvg();
    if (svg.Load(iconSourcePath) is null || svg.Picture is null)
    {
        throw new InvalidOperationException($"Failed to load SVG: {iconSourcePath}");
    }

    var picture = svg.Picture;
    var pictureBounds = picture.CullRect;

    RenderPosterLogoFromPicture(storeListingDir, "PosterArt_720x1080.png", picture, pictureBounds, 720, 1080);
    RenderPosterLogoFromPicture(storeListingDir, "PosterArt_1440x2160.png", picture, pictureBounds, 1440, 2160);
    return 0;
}

static void RenderSquareLogo(string outDir, string fileName, SKPicture picture, SKRect bounds, int width, int height)
{
    var supersample = Math.Max(width, height) >= 71 ? 2 : 1;
    var renderWidth = width * supersample;
    var renderHeight = height * supersample;

    var info = new SKImageInfo(renderWidth, renderHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var surface = SKSurface.Create(info) ?? throw new InvalidOperationException($"Failed to allocate surface {renderWidth}x{renderHeight} for {fileName}");
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);

    var scale = Math.Min(width / bounds.Width, height / bounds.Height) * supersample;
    var dx = ((renderWidth - (bounds.Width * scale)) / 2f) - (bounds.Left * scale);
    var dy = ((renderHeight - (bounds.Height * scale)) / 2f) - (bounds.Top * scale);

    canvas.Translate(dx, dy);
    canvas.Scale(scale);

    using var paint = new SKPaint
    {
        IsAntialias = true,
        FilterQuality = SKFilterQuality.High,
    };
    canvas.DrawPicture(picture, paint);

    WritePng(surface, Path.Combine(outDir, fileName), width, height);
}

static void RenderPosterLogoFromBitmap(string outDir, string fileName, SKBitmap bitmap, SKRect bounds, int width, int height)
{
    const int supersample = 2;
    var renderWidth = width * supersample;
    var renderHeight = height * supersample;
    var ss = (float)supersample;

    var info = new SKImageInfo(renderWidth, renderHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var surface = SKSurface.Create(info);
    var canvas = surface.Canvas;

    DrawPosterBackground(canvas, renderWidth, renderHeight);

    var contentBottom = height * (2f / 3f) * ss;

    var kickerSize = width * 0.026f * ss;
    var titleSize = width * 0.072f * ss;
    var taglineSize = width * 0.034f * ss;

    var kickerY = height * 0.07f * ss;
    var titleY = height * 0.115f * ss;
    var taglineY = height * 0.155f * ss;
    var textBlockBottom = height * 0.19f * ss;

    DrawPosterKicker(canvas, renderWidth, kickerY, kickerSize);
    DrawPosterBranding(canvas, renderWidth, titleY, taglineY, titleSize, taglineSize);

    var iconBandTop = textBlockBottom + height * 0.02f * ss;
    var iconBandBottom = contentBottom - height * 0.11f * ss;
    var iconBandHeight = iconBandBottom - iconBandTop;
    var iconWidthTarget = width * 0.68f * ss;
    var scaleByWidth = iconWidthTarget / bounds.Width;
    var scaleByHeight = iconBandHeight / bounds.Height;
    var scale = Math.Min(scaleByWidth, scaleByHeight);
    var scaledWidth = bounds.Width * scale;
    var scaledHeight = bounds.Height * scale;
    var iconLeft = (renderWidth - scaledWidth) / 2f;
    var iconTop = iconBandTop + ((iconBandHeight - scaledHeight) / 2f);

    using var paint = new SKPaint
    {
        IsAntialias = true,
        FilterQuality = SKFilterQuality.High,
    };
    canvas.DrawBitmap(bitmap, new SKRect(iconLeft, iconTop, iconLeft + scaledWidth, iconTop + scaledHeight), paint);

    var badgeSize = width * 0.021f * ss;
    var badgeBlockHeight = height * 0.088f * ss;
    var badgeRowY = iconTop + scaledHeight + height * 0.02f * ss + (badgeBlockHeight / 2f);
    DrawPosterFeatureBadges(canvas, renderWidth, badgeRowY, badgeSize);

    DrawPosterPaletteMockup(canvas, renderWidth, renderHeight, contentBottom);

    WritePng(surface, Path.Join(outDir, fileName), width, height);
}

static void RenderPosterLogoFromPicture(string outDir, string fileName, SKPicture picture, SKRect bounds, int width, int height)
{
    const int supersample = 2;
    var renderWidth = width * supersample;
    var renderHeight = height * supersample;
    var ss = (float)supersample;

    var info = new SKImageInfo(renderWidth, renderHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var surface = SKSurface.Create(info);
    var canvas = surface.Canvas;

    DrawPosterBackground(canvas, renderWidth, renderHeight);

    // Content lives above the bottom third; that band is decorative only (Store overlays).
    var contentBottom = height * (2f / 3f) * ss;

    var kickerSize = width * 0.026f * ss;
    var titleSize = width * 0.072f * ss;
    var taglineSize = width * 0.034f * ss;

    var kickerY = height * 0.07f * ss;
    var titleY = height * 0.115f * ss;
    var taglineY = height * 0.155f * ss;
    var textBlockBottom = height * 0.19f * ss;

    DrawPosterKicker(canvas, renderWidth, kickerY, kickerSize);
    DrawPosterBranding(canvas, renderWidth, titleY, taglineY, titleSize, taglineSize);

    // Size icon to fill the band between headline and badge row.
    var iconBandTop = textBlockBottom + height * 0.02f * ss;
    var iconBandBottom = contentBottom - height * 0.11f * ss;
    var iconBandHeight = iconBandBottom - iconBandTop;
    var iconWidthTarget = width * 0.68f * ss;
    var scaleByWidth = iconWidthTarget / bounds.Width;
    var scaleByHeight = iconBandHeight / bounds.Height;
    var scale = Math.Min(scaleByWidth, scaleByHeight);
    var scaledWidth = bounds.Width * scale;
    var scaledHeight = bounds.Height * scale;
    var dx = ((renderWidth - scaledWidth) / 2f) - (bounds.Left * scale);
    var iconTop = iconBandTop + ((iconBandHeight - scaledHeight) / 2f);
    var dy = iconTop - (bounds.Top * scale);

    canvas.Save();
    canvas.Translate(dx, dy);
    canvas.Scale(scale);

    using var paint = new SKPaint
    {
        IsAntialias = true,
        FilterQuality = SKFilterQuality.High,
    };
    canvas.DrawPicture(picture, paint);
    canvas.Restore();

    var badgeSize = width * 0.021f * ss;
    var badgeBlockHeight = height * 0.088f * ss;
    var badgeRowY = iconTop + scaledHeight + height * 0.02f * ss + (badgeBlockHeight / 2f);
    DrawPosterFeatureBadges(canvas, renderWidth, badgeRowY, badgeSize);

    DrawPosterPaletteMockup(canvas, renderWidth, renderHeight, contentBottom);

    WritePng(surface, Path.Combine(outDir, fileName), width, height);
}

static void DrawPosterBackground(SKCanvas canvas, int width, int height)
{
    var colors = new[] { new SKColor(0x26, 0x2A, 0x33), new SKColor(0x18, 0x1C, 0x24) };
    using var shader = SKShader.CreateRadialGradient(
        new SKPoint(width * 0.48f, height * 0.34f),
        height * 0.85f,
        colors,
        null,
        SKShaderTileMode.Clamp);
    using var paint = new SKPaint { Shader = shader, IsAntialias = true };
    canvas.DrawRect(0, 0, width, height, paint);
}

static void DrawPosterBranding(SKCanvas canvas, int width, float titleY, float taglineY, float titleSize, float taglineSize)
{
    using var titleTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        ?? SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
    using var bodyTypeface = SKTypeface.FromFamilyName("Segoe UI")
        ?? SKTypeface.FromFamilyName("Arial");

    using var titlePaint = new SKPaint
    {
        IsAntialias = true,
        Color = SKColors.White,
        Typeface = titleTypeface,
        TextSize = titleSize,
        TextAlign = SKTextAlign.Center,
    };
    using var taglinePaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0xA8, 0xB8, 0xC8),
        Typeface = bodyTypeface,
        TextSize = taglineSize,
        TextAlign = SKTextAlign.Center,
    };

    const string title = "Quick Shell";
    const string tagline = "Open saved folders in any terminal you use";

    var centerX = width / 2f;
    canvas.DrawText(title, centerX, titleY, titlePaint);
    canvas.DrawText(tagline, centerX, taglineY, taglinePaint);
}

static void DrawPosterKicker(SKCanvas canvas, int width, float y, float fontSize)
{
    using var typeface = SKTypeface.FromFamilyName("Segoe UI Semibold")
        ?? SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        ?? SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
    using var paint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0x5A, 0xB0, 0xE8),
        Typeface = typeface,
        TextSize = fontSize,
        TextAlign = SKTextAlign.Center,
    };

    canvas.DrawText("POWERTOYS COMMAND PALETTE", width / 2f, y, paint);
}

static void DrawPosterFeatureBadges(SKCanvas canvas, int width, float blockCenterY, float fontSize)
{
    var row1 = new[] { "Workspaces", "Favorites" };
    var row2 = new[] { "Keywords", "Git repos" };
    var pillHeight = fontSize * 1.7f;
    var rowGap = fontSize * 0.55f;
    var row1Center = blockCenterY - ((pillHeight + rowGap) / 2f);
    var row2Center = blockCenterY + ((pillHeight + rowGap) / 2f);

    DrawPosterBadgeRow(canvas, width, row1, row1Center, fontSize, pillHeight);
    DrawPosterBadgeRow(canvas, width, row2, row2Center, fontSize, pillHeight);
}

static void DrawPosterBadgeRow(
    SKCanvas canvas,
    int width,
    string[] badges,
    float rowCenterY,
    float fontSize,
    float pillHeight)
{
    var paddingH = fontSize * 0.9f;
    var gap = fontSize * 0.55f;
    var pillRadius = pillHeight / 2f;

    using var typeface = SKTypeface.FromFamilyName("Segoe UI Semibold")
        ?? SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        ?? SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
    using var textPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0xE8, 0xF0, 0xF8),
        Typeface = typeface,
        TextSize = fontSize,
        TextAlign = SKTextAlign.Left,
    };
    using var fillPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0x2F, 0x96, 0xE8, 0x22),
        Style = SKPaintStyle.Fill,
    };
    using var strokePaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0x5A, 0xB0, 0xE8, 0x55),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = Math.Max(1f, fontSize * 0.06f),
    };

    var metrics = textPaint.FontMetrics;
    var totalWidth = 0f;
    foreach (var label in badges)
    {
        totalWidth += textPaint.MeasureText(label) + (paddingH * 2f);
    }

    totalWidth += gap * (badges.Length - 1);
    var x = (width - totalWidth) / 2f;
    var pillTop = rowCenterY - (pillHeight / 2f);

    foreach (var label in badges)
    {
        var textWidth = textPaint.MeasureText(label);
        var pillWidth = textWidth + (paddingH * 2f);
        var pillRect = new SKRect(x, pillTop, x + pillWidth, pillTop + pillHeight);
        canvas.DrawRoundRect(pillRect, pillRadius, pillRadius, fillPaint);
        canvas.DrawRoundRect(pillRect, pillRadius, pillRadius, strokePaint);

        var baseline = pillRect.MidY - ((metrics.Ascent + metrics.Descent) / 2f);
        var textX = pillRect.MidX - (textWidth / 2f);
        canvas.DrawText(label, textX, baseline, textPaint);
        x += pillWidth + gap;
    }
}

static void DrawPosterPaletteMockup(SKCanvas canvas, int width, int height, float contentBottom)
{
    var mockTop = contentBottom + height * 0.018f;
    var mockHeight = height - mockTop - height * 0.035f;
    var mockWidth = width * 0.86f;
    var mockLeft = (width - mockWidth) / 2f;
    var corner = mockHeight * 0.07f;
    var frameRect = new SKRect(mockLeft, mockTop, mockLeft + mockWidth, mockTop + mockHeight);

    using var blueGlow = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateRadialGradient(
            new SKPoint(width * 0.42f, mockTop + mockHeight * 0.55f),
            mockWidth * 0.42f,
            new[] { new SKColor(0x2F, 0x96, 0xE8, 0x28), SKColors.Transparent },
            null,
            SKShaderTileMode.Clamp),
    };
    using var goldGlow = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateRadialGradient(
            new SKPoint(width * 0.62f, mockTop + mockHeight * 0.72f),
            mockWidth * 0.32f,
            new[] { new SKColor(0xF0, 0xC0, 0x38, 0x1A), SKColors.Transparent },
            null,
            SKShaderTileMode.Clamp),
    };
    canvas.DrawRect(frameRect, blueGlow);
    canvas.DrawRect(frameRect, goldGlow);

    using var fillPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0x12, 0x16, 0x1E, 0xCC),
        Style = SKPaintStyle.Fill,
    };
    using var framePaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0xFF, 0xFF, 0xFF, 0x18),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = height * 0.0025f,
    };
    canvas.DrawRoundRect(frameRect, corner, corner, fillPaint);
    canvas.DrawRoundRect(frameRect, corner, corner, framePaint);

    var titleBarHeight = mockHeight * 0.14f;
    using var titleTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        ?? SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
    using var bodyTypeface = SKTypeface.FromFamilyName("Segoe UI")
        ?? SKTypeface.FromFamilyName("Arial");
    using var titlePaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0xFF, 0xFF, 0xFF, 0x90),
        Typeface = titleTypeface,
        TextSize = width * 0.028f,
        TextAlign = SKTextAlign.Left,
    };
    using var hintPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0x7E, 0x96, 0xAE, 0x80),
        Typeface = bodyTypeface,
        TextSize = width * 0.022f,
        TextAlign = SKTextAlign.Left,
    };
    var chromeX = mockLeft + mockWidth * 0.06f;
    var chromeMetrics = titlePaint.FontMetrics;
    var chromeBaseline = mockTop + titleBarHeight * 0.62f - ((chromeMetrics.Ascent + chromeMetrics.Descent) / 2f);
    canvas.DrawText("Command Palette", chromeX, chromeBaseline, titlePaint);
    canvas.DrawText("Win+Alt+Space", mockLeft + mockWidth * 0.94f - hintPaint.MeasureText("Win+Alt+Space"), chromeBaseline, hintPaint);

    var searchTop = mockTop + titleBarHeight + mockHeight * 0.04f;
    var searchHeight = mockHeight * 0.11f;
    var searchLeft = mockLeft + mockWidth * 0.06f;
    var searchWidth = mockWidth * 0.88f;
    var searchRect = new SKRect(searchLeft, searchTop, searchLeft + searchWidth, searchTop + searchHeight);
    using var searchFill = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0xFF, 0xFF, 0xFF, 0x08),
        Style = SKPaintStyle.Fill,
    };
    using var searchStroke = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0x5A, 0xB0, 0xE8, 0x45),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = height * 0.0018f,
    };
    canvas.DrawRoundRect(searchRect, searchHeight * 0.28f, searchHeight * 0.28f, searchFill);
    canvas.DrawRoundRect(searchRect, searchHeight * 0.28f, searchHeight * 0.28f, searchStroke);

    using var searchTextPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0xFF, 0xFF, 0xFF, 0x88),
        Typeface = bodyTypeface,
        TextSize = width * 0.028f,
        TextAlign = SKTextAlign.Left,
    };
    var searchMetrics = searchTextPaint.FontMetrics;
    var searchBaseline = searchRect.MidY - ((searchMetrics.Ascent + searchMetrics.Descent) / 2f);
    canvas.DrawText("api", searchLeft + searchWidth * 0.05f, searchBaseline, searchTextPaint);

    var listTop = searchTop + searchHeight + mockHeight * 0.05f;
    var rowHeight = mockHeight * 0.19f;
    DrawPaletteListRow(
        canvas,
        mockLeft + mockWidth * 0.06f,
        listTop,
        mockWidth * 0.88f,
        rowHeight,
        "My API",
        "~/Projects/MyApi · PowerShell · dotnet run · home · api",
        selected: true);
    DrawPaletteListRow(
        canvas,
        mockLeft + mockWidth * 0.06f,
        listTop + rowHeight + mockHeight * 0.02f,
        mockWidth * 0.88f,
        rowHeight,
        "Frontend",
        "~/Projects/web · npm run dev · PowerShell",
        selected: false);
}

static void DrawPaletteListRow(
    SKCanvas canvas,
    float left,
    float top,
    float width,
    float height,
    string title,
    string subtitle,
    bool selected)
{
    var rowRect = new SKRect(left, top, left + width, top + height);
    if (selected)
    {
        using var selectionPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0x2F, 0x96, 0xE8, 0x24),
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawRoundRect(rowRect, height * 0.14f, height * 0.14f, selectionPaint);
    }

    using var titleTypeface = SKTypeface.FromFamilyName("Segoe UI Semibold")
        ?? SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        ?? SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
    using var bodyTypeface = SKTypeface.FromFamilyName("Segoe UI")
        ?? SKTypeface.FromFamilyName("Arial");
    var titleSize = height * 0.28f;
    var subtitleSize = height * 0.17f;
    using var titlePaint = new SKPaint
    {
        IsAntialias = true,
        Color = selected ? SKColors.White : new SKColor(0xE8, 0xF0, 0xF8, 0xCC),
        Typeface = titleTypeface,
        TextSize = titleSize,
        TextAlign = SKTextAlign.Left,
    };
    using var subtitlePaint = new SKPaint
    {
        IsAntialias = true,
        Color = selected ? new SKColor(0xA8, 0xB8, 0xC8, 0xCC) : new SKColor(0x7E, 0x96, 0xAE, 0xAA),
        Typeface = bodyTypeface,
        TextSize = subtitleSize,
        TextAlign = SKTextAlign.Left,
    };

    var titleMetrics = titlePaint.FontMetrics;
    var subtitleMetrics = subtitlePaint.FontMetrics;
    var textX = left + width * 0.04f;
    var titleBaseline = top + height * 0.38f - ((titleMetrics.Ascent + titleMetrics.Descent) / 2f);
    var subtitleBaseline = top + height * 0.72f - ((subtitleMetrics.Ascent + subtitleMetrics.Descent) / 2f);
    canvas.DrawText(title, textX, titleBaseline, titlePaint);
    canvas.DrawText(TruncateToWidth(subtitle, subtitlePaint, width * 0.92f), textX, subtitleBaseline, subtitlePaint);
}

static string TruncateToWidth(string text, SKPaint paint, float maxWidth)
{
    if (paint.MeasureText(text) <= maxWidth)
    {
        return text;
    }

    const string ellipsis = "…";
    var trimmed = text;
    while (trimmed.Length > 0 && paint.MeasureText(trimmed + ellipsis) > maxWidth)
    {
        trimmed = trimmed[..^1];
    }

    return trimmed.Length == 0 ? ellipsis : trimmed + ellipsis;
}

static void RenderSvgToPng(string svgPath, string outPath, int width, int height)
{
    if (!File.Exists(svgPath))
    {
        throw new FileNotFoundException($"Missing SVG: {svgPath}");
    }

    using var svg = new SKSvg();
    if (svg.Load(svgPath) is null || svg.Picture is null)
    {
        throw new InvalidOperationException($"Failed to load SVG: {svgPath}");
    }

    var bounds = svg.Picture.CullRect;
    var supersample = Math.Max(width, height) >= 71 ? 2 : 1;
    var renderWidth = width * supersample;
    var renderHeight = height * supersample;

    var info = new SKImageInfo(renderWidth, renderHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var surface = SKSurface.Create(info) ?? throw new InvalidOperationException($"Failed to allocate surface {renderWidth}x{renderHeight} for {Path.GetFileName(svgPath)}");
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);

    var scale = Math.Min(width / bounds.Width, height / bounds.Height) * supersample;
    var dx = ((renderWidth - (bounds.Width * scale)) / 2f) - (bounds.Left * scale);
    var dy = ((renderHeight - (bounds.Height * scale)) / 2f) - (bounds.Top * scale);

    canvas.Translate(dx, dy);
    canvas.Scale(scale);

    using var paint = new SKPaint
    {
        IsAntialias = true,
        FilterQuality = SKFilterQuality.High,
    };
    canvas.DrawPicture(svg.Picture, paint);

    WritePng(surface, outPath, width, height);
}

static void WritePng(SKSurface surface, string outPath, int width, int height)
{
    using var rendered = surface.Snapshot();
    if (rendered.Width != width || rendered.Height != height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var destSurface = SKSurface.Create(info) ?? throw new InvalidOperationException($"Failed to allocate resize surface {width}x{height} for {outPath}");
        var canvas = destSurface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High,
        };
        canvas.DrawImage(
            rendered,
            SKRect.Create(rendered.Width, rendered.Height),
            SKRect.Create(width, height),
            paint);

        using var final = destSurface.Snapshot();
        using var data = final.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outPath);
        data.SaveTo(stream);
    }
    else
    {
        using var data = rendered.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outPath);
        data.SaveTo(stream);
    }

    Console.WriteLine($"Wrote {outPath} ({width}x{height})");
}

static void CopyIfExists(string sourceDir, string sourceName, string destName)
{
    var sourcePath = Path.Combine(sourceDir, sourceName);
    var destPath = Path.Combine(sourceDir, destName);
    if (File.Exists(sourcePath))
    {
        File.Copy(sourcePath, destPath, overwrite: true);
        Console.WriteLine($"Wrote {destPath}");
    }
}
