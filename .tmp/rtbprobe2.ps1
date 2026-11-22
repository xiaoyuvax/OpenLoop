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
$need = $rtb.GetPositionFromCharIndex($last).Y + $rtb.Font.Height + 4
$rtb.Height = $need
[System.Windows.Forms.Application]::DoEvents()
"grow: height=$($rtb.Height) firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"

$rtb.SelectionStart = $last
$rtb.ScrollToCaret()
[System.Windows.Forms.Application]::DoEvents()
"scrolled to end: firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"

$rtb.Height = $need - 100
[System.Windows.Forms.Application]::DoEvents()
"shrink 100: height=$($rtb.Height) firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"

$rtb.Height = $need
[System.Windows.Forms.Application]::DoEvents()
"grow back: height=$($rtb.Height) firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"

# click simulation: caret mid text then shrink
$rtb.SelectionStart = 300
$rtb.ScrollToCaret()
[System.Windows.Forms.Application]::DoEvents()
"mid caret: firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
$rtb.Height = $need - 60
[System.Windows.Forms.Application]::DoEvents()
"shrink 60: height=$($rtb.Height) firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
$rtb.Height = $need
[System.Windows.Forms.Application]::DoEvents()
"grow back 2: height=$($rtb.Height) firstY=$($rtb.GetPositionFromCharIndex(0).Y) lastY=$($rtb.GetPositionFromCharIndex($last).Y)"
$form.Close()
