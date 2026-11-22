Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

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
[System.Windows.Forms.Application]::DoEvents()

$last = $rtb.TextLength - 1
$pos = $rtb.GetPositionFromCharIndex($last)
$need = $pos.Y + $rtb.Font.Height + 4
"fontH=$($rtb.Font.Height) lastY=$($pos.Y) need=$need height=$($rtb.Height) firstY=$($rtb.GetPositionFromCharIndex(0).Y)"

$rtb.Height = $need
[System.Windows.Forms.Application]::DoEvents()
"after grow: height=$($rtb.Height) firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"

$rtb.Focus()
[System.Windows.Forms.Application]::DoEvents()
"focused=$($rtb.Focused) firstY=$($rtb.GetPositionFromCharIndex(0).Y)"

$rtb.SelectionStart = $last
$rtb.SelectionLength = 1
[System.Windows.Forms.Application]::DoEvents()
"after sel last: firstY=$($rtb.GetPositionFromCharIndex(0).Y)"
$rtb.SelectionStart = $rtb.TextLength
$rtb.SelectionLength = 0
[System.Windows.Forms.Application]::DoEvents()
"after sel end: firstY=$($rtb.GetPositionFromCharIndex(0).Y)"

$rtb.Height = $need - 30
[System.Windows.Forms.Application]::DoEvents()
"after shrink 30: height=$($rtb.Height) firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"

$rtb.Height = $need
[System.Windows.Forms.Application]::DoEvents()
"after grow back: height=$($rtb.Height) firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"

$form.Close()
