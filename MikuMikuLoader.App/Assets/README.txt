Put your app icon here as: icon.ico

The .csproj references Assets\icon.ico for the .exe icon, and MainWindow.axaml
references /Assets/icon.ico for the window/taskbar icon. The build will FAIL until
icon.ico exists in this folder. A 256x256 .ico is recommended.
