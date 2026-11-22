Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class U {
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
}
"@
$form = New-Object System.Windows.Forms.Form
$form.Size = New-Object System.Drawing.Size(600, 500)
$rtb = New-Object System.Windows.Forms.RichTextBox
$rtb.Multiline = $true
$rtb.WordWrap = $true
$rtb.ScrollBars = [System.Windows.Forms.RichTextBoxScrollBars]::Vertical
$rtb.ReadOnly = $true
$rtb.Width = 400
$rtb.Height = 200
$rtb.Location = New-Object System.Drawing.Point(10, 10)
$form.Controls.Add($rtb)
$text = (1..40 | ForEach-Object { "line $_ some text here to wrap a bit more content" }) -join "`n"
$rtb.Text = $text
$form.Show()
[System.Windows.Forms.Application]::DoEvents()
$last = $rtb.TextLength - 1
$need = $rtb.GetPositionFromCharIndex($last).Y + $rtb.Font.Height + 4
$rtb.Height = $need
[System.Windows.Forms.Application]::DoEvents()
"grow: height=$($rtb.Height) firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
$rtb.Height = 200
[System.Windows.Forms.Application]::DoEvents()
"shrink: height=$($rtb.Height) firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
# WM_VSCROLL = 0x0115, SB_LINEDOWN = 1
for ($i=0; $i -lt 5; $i++) { [U]::SendMessage($rtb.Handle, 0x0115, [IntPtr]1, [IntPtr]::Zero) | Out-Null }
[System.Windows.Forms.Application]::DoEvents()
"after scroll down 5: firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
$rtb.SelectionStart = $last
[System.Windows.Forms.Application]::DoEvents()
"sel last: firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
$rtb.ScrollToCaret()
[System.Windows.Forms.Application]::DoEvents()
"scrolltocaret: firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
$form.Close()
