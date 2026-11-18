# Serves the website in .\docs at http://localhost:8765 for previewing changes locally.
# Usage: .\tools\serve-site.ps1   (Ctrl+C to stop)
param([int]$Port = 8765)

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\docs')).Path
$types = @{
    '.html' = 'text/html; charset=utf-8'; '.css' = 'text/css'; '.js' = 'text/javascript'
    '.png' = 'image/png'; '.ico' = 'image/x-icon'; '.svg' = 'image/svg+xml'; '.json' = 'application/json'
}

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "Serving $root at http://localhost:$Port/  (Ctrl+C to stop)"

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $rel = [Uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath.TrimStart('/'))
        if ($rel -eq '' -or $rel.EndsWith('/')) { $rel += 'index.html' }
        $path = [IO.Path]::GetFullPath((Join-Path $root $rel))
        if ($path.StartsWith($root) -and (Test-Path $path -PathType Leaf)) {
            $bytes = [IO.File]::ReadAllBytes($path)
            $ext = [IO.Path]::GetExtension($path).ToLower()
            $ctx.Response.ContentType = $(if ($types.ContainsKey($ext)) { $types[$ext] } else { 'application/octet-stream' })
            $ctx.Response.ContentLength64 = $bytes.Length
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        } else {
            $ctx.Response.StatusCode = 404
        }
        $ctx.Response.Close()
    }
} finally {
    $listener.Stop()
}
