
# read in the content from a dynamic pode file and invoke its content
function ConvertFrom-PodeFile
{
    param (
        [Parameter(Mandatory=$true)]
        [ValidateNotNull()]
        $Content,

        [Parameter()]
        $Data = @{}
    )

    # if we have data, then setup the data param
    if (!(Test-Empty $Data)) {
        $Content = "param(`$data)`nreturn `"$($Content -replace '"', '``"')`""
    }
    else {
        $Content = "return `"$($Content -replace '"', '``"')`""
    }

    # invoke the content as a script to generate the dynamic content
    $Content = (Invoke-Command -ScriptBlock ([scriptblock]::Create($Content)) -ArgumentList $Data)
    return $Content
}

function Get-Type
{
    param (
        [Parameter()]
        $Value
    )

    if ($Value -eq $null) {
        return $null
    }

    return @{
        'Name' = $Value.GetType().Name.ToLowerInvariant();
        'BaseName' = $Value.GetType().BaseType.Name.ToLowerInvariant();
    }
}

function Test-Empty
{
    param (
        [Parameter()]
        $Value
    )

    $type = Get-Type $Value
    if ($type -eq $null) {
        return $true
    }

    if ($type.Name -ieq 'string') {
        return [string]::IsNullOrWhiteSpace($Value)
    }

    if ($type.Name -ieq 'hashtable') {
        return $Value.Count -eq 0
    }

    switch ($type.BaseName) {
        'valuetype' {
            return $false
        }

        'array' {
            return (($Value | Measure-Object).Count -eq 0 -or $Value.Count -eq 0)
        }
    }

    return ([string]::IsNullOrWhiteSpace($Value) -or ($Value | Measure-Object).Count -eq 0 -or $Value.Count -eq 0)
}

function Get-DynamicContentType
{
    param (
        [Parameter()]
        [string]
        $Path
    )

    # default content type
    $ctype = 'text/plain'

    # if no path, return default
    if (Test-Empty $Path) {
        return $ctype
    }

    # get secondary extension (like style.css.pode would be css)
    $ext = [System.IO.Path]::GetExtension([System.IO.Path]::GetFileNameWithoutExtension($Path)).Trim('.')

    # get content type from secondary extension
    switch ($ext.ToLowerInvariant()) {
        'css' { $ctype = 'text/css' }
        'js' { $ctype = 'text/javascript' }
    }

    return $ctype
}

function Add-PodeRunspace
{
    param (
        [Parameter(Mandatory=$true)]
        [ValidateNotNull()]
        [scriptblock]
        $ScriptBlock
    )

    $ps = [powershell]::Create()
    $ps.RunspacePool = $PodeSession.RunspacePool
    $ps.AddScript($ScriptBlock) | Out-Null

    $PodeSession.Runspaces += @{
        'Runspace' = $ps;
        'Status' = $ps.BeginInvoke();
        'Stopped' = $false;
    }
}

function Close-PodeRunspaces
{
    $PodeSession.Runspaces | Where-Object { !$_.Stopped } | ForEach-Object {
        $_.Runspace.Dispose()
        $_.Stopped = $true
    }

    if (!$PodeSession.RunspacePool.IsDisposed) {
        $PodeSession.RunspacePool.Close()
        $PodeSession.RunspacePool.Dispose()
    }
}

function Test-CtrlCPressed
{
    if ([Console]::IsInputRedirected -or ![Console]::KeyAvailable) {
        return
    }

    $key = [Console]::ReadKey($true)

    if ($key.Key -ieq 'c' -and $key.Modifiers -band [ConsoleModifiers]::Control) {
        Write-Host 'Terminating...' -NoNewline
        Close-PodeRunspaces
        Write-Host " Done" -ForegroundColor Green
        exit 0
    }
}