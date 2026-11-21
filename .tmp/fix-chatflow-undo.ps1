$p = 'D:\PUB\OpenLoop\src\ChatFlow\ChatFlow\ChatMessageRow.cs'
$old = @'
            g.DrawArc(pen, cx - 6 * k, cy - 6 * k, 12 * k, 12 * k, 35, 285);
            using var brush = new SolidBrush(color);
            g.FillPolygon(brush,
            [
                new PointF(cx - 0.6f * k, cy - 7.8f * k),
                new PointF(cx - 6.6f * k, cy - 5.4f * k),
                new PointF(cx - 1.8f * k, cy - 1.8f * k)
            ]);
'@
$new = @'
            // Gap sits at upper-left; the arrow head is attached to the arc end. The old
            // triangle floated inside the ring, so the arrow looked like it was in the middle.
            const float startDeg = 260f;
            g.DrawArc(pen, cx - 6 * k, cy - 6 * k, 12 * k, 12 * k, startDeg, 300);

            double rad = startDeg * Math.PI / 180.0;
            float sin = (float)Math.Sin(rad), cos = (float)Math.Cos(rad);
            float px = cx + 6f * k * cos, py = cy + 6f * k * sin;
            float ux = sin, uy = -cos;   // tangent towards the gap (decreasing angle)
            float nx = -uy, ny = ux;     // perpendicular to the arrow base
            using var brush = new SolidBrush(color);
            g.FillPolygon(brush,
            [
                new PointF(px + ux * 2.3f * k, py + uy * 2.3f * k),
                new PointF(px - ux * 0.45f * k + nx * 1.7f * k, py - uy * 0.45f * k + ny * 1.7f * k),
                new PointF(px - ux * 0.45f * k - nx * 1.7f * k, py - uy * 0.45f * k - ny * 1.7f * k)
            ]);
'@

$txt = [System.IO.File]::ReadAllText($p)
$nl = if ($txt.Contains("`r`n")) { "`r`n" } else { "`n" }
$old = $old.Replace("`r`n", "`n").Replace("`n", $nl)
$new = $new.Replace("`r`n", "`n").Replace("`n", $nl)

if (-not $txt.Contains($old)) { Write-Output 'NOT_FOUND'; exit 1 }
[System.IO.File]::WriteAllText($p, $txt.Replace($old, $new), (New-Object System.Text.UTF8Encoding($false)))
Write-Output 'REPLACED'
