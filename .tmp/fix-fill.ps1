$ErrorActionPreference = 'Stop'
$path = 'D:\PUB\OpenLoop\src\openloop\src\OpenLoop.App\ModelChoices.cs'
$text = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
$recentLabel = -join ([char]0x6700,[char]0x8FD1,[char]0x4F7F,[char]0x7528)
$allLabel = -join ([char]0x5168,[char]0x90E8,[char]0x6A21,[char]0x578B)
$freeLabel = -join ([char]0x514D,[char]0x8D39)
$nl = "`r`n"
$body = @'
public static void Fill(AntdUI.Select select, IEnumerable<ModelInfo> models, AppSettings settings)
{
    var all = models as IList<ModelInfo> ?? models.ToList();
    var recent = Recent(all, settings);
    var recentIds = recent.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    select.Items.Clear();
    if (recent.Count > 0)
    {
        select.Items.Add(Header(RECENT));
        foreach (var model in recent) select.Items.Add(Item(model));
        if (recent.Count < all.Count) select.Items.Add(Header(ALL));
    }

    foreach (var model in all.Where(m => !recentIds.Contains(m.Id))) select.Items.Add(Item(model));
}

private static AntdUI.SelectItem Item(ModelInfo model) => new(model.Name)
{
    Tag = model.Id,
    IconSvg = model.IsFree ? UiIcons.Dot(UiStyle.Success) : null,
    SubText = model.IsFree ? FREE : null
};

/// <summary>分组标题：不能选，只当分隔；240dpi 下 1px 细线看不见，所以用一行灰字。</summary>
private static AntdUI.SelectItem Header(string text) => new(text)
{
    Tag = Divider,
    Enable = false,
    Fore = UiStyle.Muted
};
'@
$body = $body.Replace('RECENT', ('"' + $recentLabel + '"')).Replace('ALL', ('"' + $allLabel + '"')).Replace('FREE', ('"' + $freeLabel + '"'))
$lines = $body -split "`r`n"
$indented = @()
for ($i = 0; $i -lt $lines.Count; $i++) { if ($i -eq 0) { $indented += $lines[$i] } else { $indented += ('        ' + $lines[$i]) } }
$replacement = ($indented -join $nl) + $nl + $nl + '        '
$pattern = '(?s)public static void Fill\(AntdUI\.Select select.*?(?=private static List<ModelInfo> Recent\()'
if (-not [regex]::IsMatch($text, $pattern)) { throw 'anchor not found' }
$text = [regex]::Replace($text, $pattern, $replacement)
[IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($true)))
'written'
