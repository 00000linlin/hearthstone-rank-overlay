Unicode true
!include "LogicLib.nsh"
Name "旅店排名 1.0.0"
OutFile "dist\HearthstoneRank-1.0.0-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\HearthstoneRank"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 1
Icon "build\app.ico"
UninstallIcon "build\app.ico"
ShowInstDetails show
ShowUninstDetails show
Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles
DirText "安装旅店排名。不需要管理员权限；不会安装 Python、浏览器或额外运行时。"

Function .onInit
  SetShellVarContext current
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 < 378389
    MessageBox MB_OK|MB_ICONSTOP "需要 .NET Framework 4.5 或更新版本。Windows 10 / 11 自带兼容版本。"
    Abort
  ${EndIf}
FunctionEnd

Section "旅店排名"
  IfFileExists "$INSTDIR\HearthstoneRank.exe" 0 +3
    ExecWait '"$INSTDIR\HearthstoneRank.exe" --shutdown'
    Sleep 800
  SetOutPath "$INSTDIR"
  File "dist\HearthstoneRank.exe"
  File "dist\HearthstoneRank.exe.config"
  File "dist\LICENSE.txt"
  File "dist\HearthstoneRank-README.md"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "DisplayName" "旅店排名"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "DisplayVersion" "1.0.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "DisplayIcon" "$INSTDIR\HearthstoneRank.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "NoRepair" 1
  CreateDirectory "$SMPROGRAMS\旅店排名"
  CreateShortcut "$SMPROGRAMS\旅店排名\旅店排名.lnk" "$INSTDIR\HearthstoneRank.exe"
  CreateShortcut "$SMPROGRAMS\旅店排名\设置.lnk" "$INSTDIR\HearthstoneRank.exe" "--settings"
  CreateShortcut "$SMPROGRAMS\旅店排名\卸载.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  ExecWait '"$INSTDIR\HearthstoneRank.exe" --shutdown'
  Sleep 800
  Delete "$INSTDIR\HearthstoneRank.exe"
  Delete "$INSTDIR\HearthstoneRank.exe.config"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\HearthstoneRank-README.md"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\旅店排名\旅店排名.lnk"
  Delete "$SMPROGRAMS\旅店排名\设置.lnk"
  Delete "$SMPROGRAMS\旅店排名\卸载.lnk"
  RMDir "$SMPROGRAMS\旅店排名"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank"
  ; Keep the user's settings. Never recursively delete the install directory.
SectionEnd
