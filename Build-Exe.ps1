param()

$ErrorActionPreference='Stop'
$root=Split-Path -Parent $MyInvocation.MyCommand.Path
$csc='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs=@(
 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll',
 'C:\Windows\Microsoft.NET\assembly\GAC_32\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll',
 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll',
 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll'
)
$args=@(
 '/nologo','/target:winexe','/platform:anycpu','/optimize+',
 ('/out:'+[IO.Path]::Combine($root,'流光日程.exe')),
 ('/win32icon:'+[IO.Path]::Combine($root,'桌面日程.ico')),
 ('/win32manifest:'+[IO.Path]::Combine($root,'app.manifest')),
 ('/resource:'+([IO.Path]::Combine($root,'UI.xaml'))+',UI.xaml'),
 ('/resource:'+([IO.Path]::Combine($root,'DesktopTodo.cs'))+',DesktopTodo.cs'),
 ('/resource:'+([IO.Path]::Combine($root,'Motion.cs'))+',Motion.cs')
)+($refs|ForEach-Object{'/reference:'+$_})+@([IO.Path]::Combine($root,'DesktopTodo.cs'),[IO.Path]::Combine($root,'Motion.cs'))
& $csc $args
if($LASTEXITCODE-ne0){throw '编译失败'}
Write-Output '流光日程.exe 编译成功。'
