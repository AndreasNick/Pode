$path = Split-Path -Parent -Path (Split-Path -Parent -Path $MyInvocation.MyCommand.Path)
Import-Module "$($path)/src/Pode.psm1" -Force -ErrorAction Stop

# or just:
# Import-Module Pode

# create a server, and start listening
Start-PodeServer {

    # listen
    Add-PodeEndpoint -Address * -Port 8090 -Protocol Http

    # log requests to the terminal
    New-PodeLoggingMethod -Terminal | Enable-PodeErrorLogging

    # GET request for web page on "localhost:8085/"
    Add-PodeRoute -Method Get -Path '/' -ScriptBlock {
        Write-PodeJsonResponse -Value @{
            Kenobi = 'Hello, there'
        }
    }
}