$repo = $PSScriptRoot
$out = Join-Path $repo 'dist'
$app = Join-Path $out 'Yinka.app'
$web = Join-Path $app 'Contents\Resources\web'
Remove-Item -Recurse -Force $app -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $app 'Contents\MacOS'), (Join-Path $app 'Contents\Resources'), (Join-Path $web 'js'), (Join-Path $web 'Data')
Copy-Item (Join-Path $repo 'Yinka.Mac\index.html') $web
Copy-Item (Join-Path $repo 'Yinka.Mac\broadcast.html') $web
Copy-Item (Join-Path $repo 'Yinka.Mac\style.css') $web
Copy-Item (Join-Path $repo 'Yinka.Mac\broadcast.css') $web
Copy-Item (Join-Path $repo 'Yinka.Mac\js') $web -Recurse
Copy-Item (Join-Path $repo 'Data\en_kjv.json') (Join-Path $web 'Data')
Copy-Item (Join-Path $repo 'Yinka.Mac\server\yinka_server.py') $web
Copy-Item (Join-Path $repo 'Yinka.Mac\server\launcher.sh') (Join-Path $app 'Contents\MacOS\Yinka')
Copy-Item (Join-Path $repo 'Yinka.Mac\server\Info.plist') (Join-Path $app 'Contents\Info.plist')
$tmp = New-TemporaryFile | % { Remove-Item $_; New-Item -ItemType Directory $_ }
$png = Join-Path $tmp 'AppIcon-1024.png'
python3 (Join-Path $repo 'Yinka.Mac\server\make_icon.py') $png
Copy-Item $png (Join-Path $app 'Contents\Resources\AppIcon.png')
Remove-Item -Recurse $tmp
(Get-Item $app).LastWriteTime = Get-Date
Write-Host "Built $app"