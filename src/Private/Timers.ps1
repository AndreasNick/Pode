function Find-PodeTimer
{
    param (
        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [string]
        $Name
    )

    return $PodeContext.Timers[$Name]
}

function Start-PodeTimerRunspace
{
    if ((Get-PodeCount $PodeContext.Timers) -eq 0) {
        return
    }

    $script = {
        while ($true)
        {
            $_now = [DateTime]::Now

            # only run timers that haven't completed, and have a next trigger in the past
            $PodeContext.Timers.Values | Where-Object {
                !$_.Completed -and ($_.OnStart -or ($_.NextTriggerTime -le $_now))
            } | ForEach-Object {
                $_.OnStart = $false
                $_.Count++

                # set last trigger to current next trigger
                if ($null -ne $_.NextTriggerTime) {
                    $_.LastTriggerTime = $_.NextTriggerTime
                }
                else {
                    $_.LastTriggerTime = [datetime]::Now
                }

                # has the timer completed?
                if (($_.Limit -gt 0) -and ($_.Count -ge $_.Limit)) {
                    $_.Completed = $true
                }

                # next trigger
                if (!$_.Completed) {
                    $_.NextTriggerTime = $_now.AddSeconds($_.Interval)
                }
                else {
                    $_.NextTriggerTime = $null
                }

                # run the timer
                Invoke-PodeInternalTimer -Timer $_
            }

            Start-Sleep -Seconds 1
        }
    }

    Add-PodeRunspace -Type Main -ScriptBlock $script
}

function Invoke-PodeInternalTimer
{
    param(
        [Parameter(Mandatory=$true)]
        $Timer
    )

    try {
        $global:TimerEvent = @{
            Lockable = $PodeContext.Lockables.Global
            Sender = $Timer
        }

        $_args = @($Timer.Arguments)
        if ($null -ne $Timer.UsingVariables) {
            $_vars = @()
            foreach ($_var in $Timer.UsingVariables) {
                $_vars += ,$_var.Value
            }
            $_args = $_vars + $_args
        }

        Invoke-PodeScriptBlock -ScriptBlock $Timer.Script -Arguments $_args -Scoped -Splat
    }
    catch {
        $_ | Write-PodeErrorLog
    }
}