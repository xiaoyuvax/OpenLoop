Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class U2 {
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
}
"@
$form = New-Object System.Windows.Forms.Form
$form.Size = New-Object System.Drawing.Size(600, 500)
$rtb = New-Object System.Windows.Forms.RichTextBox
$rtb.Multiline = $true
$rtb.WordWrap = $true
$rtb.ScrollBars = [System.Windows.Forms.RichTextBoxScrollBars]::None
$rtb.ReadOnly = $true
$rtb.BorderStyle = [System.Windows.Forms.BorderStyle]::None
$rtb.Width = 400
$rtb.Height = 200
$rtb.Location = New-Object System.Drawing.Point(10, 10)
$form.Controls.Add($rtb)
$text = (1..40 | ForEach-Object { "line $_ some text here to wrap a bit more content" }) -join "`n"
$rtb.Text = $text
$form.Show()
$rtb.Focus()
[System.Windows.Forms.Application]::DoEvents()
"focused=$($rtb.Focused)"
$last = $rtb.TextLength - 1
"before: firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
$rtb.SelectionStart = $last
$rtb.SelectionLength = 1
[System.Windows.Forms.Application]::DoEvents()
"sel last (focused): firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
$rtb.SelectionStart = $rtb.TextLength
[System.Windows.Forms.Application]::DoEvents()
"sel end: firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
$rtb.SelectionStart = $last
$rtb.ScrollToCaret()
[System.Windows.Forms.Application]::DoEvents()
"scrolltocaret: firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
for ($i=0; $i -lt 5; $i++) { [U2]::SendMessage($rtb.Handle, 0x0115, [IntPtr]1, [IntPtr]::Zero) | Out-Null }
[System.Windows.Forms.Application]::DoEvents()
"wm_vscroll: firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
$form.Close()
