Unicode true
!include "LogicLib.nsh"
!ifndef VERSION
  !error "Run build.ps1 -Installer to supply VERSION and a verified payload."
!endif
Name "旅店排名 ${VERSION}"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\HearthstoneRank"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 1
Icon "${PAYLOAD_DIR}\app.ico"
UninstallIcon "${PAYLOAD_DIR}\app.ico"
ShowInstDetails show
ShowUninstDetails show
Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles
DirText "当前用户安装。请选择空目录或已安装旅店排名的目录；不支持链接或网络目录。"

!macro GuardFunction PREFIX
Function ${PREFIX}Guard
  ; Only execute the helper extracted from this installer's own payload.
  nsExec::ExecToStack /TIMEOUT=5000 '"$PLUGINSDIR\InstallerGuard.exe" $0'
  Pop $1
  Pop $2
FunctionEnd
!macroend
!insertmacro GuardFunction ""
!insertmacro GuardFunction "un."

!macro AcquireInstallerMutex
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\HearthstoneRank.Installer") p .r9 ?e'
  Pop $0
  StrCmp $9 "0" installer_busy
  StrCmp $0 "183" installer_busy
  Goto installer_lock_acquired
  installer_busy:
    MessageBox MB_OK|MB_ICONEXCLAMATION "另一个安装或卸载操作正在运行，请完成后再试。" /SD IDOK
    SetErrorLevel 2
    Abort
  installer_lock_acquired:
!macroend

!macro DeletePayload FILE
  IfFileExists "$INSTDIR\${FILE}" 0 +4
    ClearErrors
    Delete "$INSTDIR\${FILE}"
    IfErrors uninstall_failed
!macroend

Function .onInit
  !insertmacro AcquireInstallerMutex
  SetShellVarContext current
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 < 378389
    MessageBox MB_OK|MB_ICONSTOP "需要 .NET Framework 4.5 或更新版本。" /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "${PAYLOAD_DIR}\InstallerGuard.exe"
FunctionEnd

Function un.onInit
  !insertmacro AcquireInstallerMutex
  SetShellVarContext current
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "${PAYLOAD_DIR}\InstallerGuard.exe"
FunctionEnd

Function .onVerifyInstDir
  StrCpy $0 '--check-install "$INSTDIR"'
  Call Guard
  StrCmp $1 "0" valid
  Abort
  valid:
FunctionEnd

Section "旅店排名"
  StrCpy $0 '--check-install "$INSTDIR"'
  Call Guard
  StrCmp $1 "0" safe
    DetailPrint "安装目录校验失败：$2"
    MessageBox MB_OK|MB_ICONSTOP "安装目录不安全或不属于本程序。$\r$\n$2" /SD IDOK
    SetErrorLevel 2
    Abort
  safe:
  StrCpy $0 '--shutdown'
  Call Guard
  StrCmp $1 "0" stopped
    DetailPrint "无法安全通知旧程序退出：$2"
    SetErrorLevel 2
    Abort
  stopped:
  Sleep 500
  ClearErrors
  SetOutPath "$INSTDIR"
  ; Claim the validated empty/owned directory before extracting any payload,
  ; so an interrupted copy can be safely retried instead of looking foreign.
  FileOpen $0 "$INSTDIR\HearthstoneRank.install" w
  IfErrors install_failed
  FileWrite $0 "HearthstoneRank-managed-directory-v1"
  FileClose $0
  IfErrors install_failed
  File "${PAYLOAD_DIR}\HearthstoneRank.exe"
  File "${PAYLOAD_DIR}\HearthstoneRank.exe.config"
  File "${PAYLOAD_DIR}\LICENSE.txt"
  File "${PAYLOAD_DIR}\HearthstoneRank-README.md"
  IfErrors install_failed
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  IfErrors install_failed
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "DisplayName" "旅店排名"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "DisplayIcon" "$INSTDIR\HearthstoneRank.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank" "NoRepair" 1
  IfErrors install_failed
  CreateDirectory "$SMPROGRAMS\旅店排名"
  CreateShortcut "$SMPROGRAMS\旅店排名\旅店排名.lnk" "$INSTDIR\HearthstoneRank.exe"
  CreateShortcut "$SMPROGRAMS\旅店排名\设置.lnk" "$INSTDIR\HearthstoneRank.exe" "--settings"
  CreateShortcut "$SMPROGRAMS\旅店排名\卸载.lnk" "$INSTDIR\Uninstall.exe"
  IfErrors install_failed
  Goto install_done
  install_failed:
    DetailPrint "安装未完成；旧卸载记录不会被提前移除。"
    SetErrorLevel 2
    Abort
  install_done:
SectionEnd

Section "Uninstall"
  StrCpy $0 '--check-uninstall "$INSTDIR"'
  Call un.Guard
  StrCmp $1 "0" safe
    DetailPrint "卸载目录校验失败：$2"
    MessageBox MB_OK|MB_ICONSTOP "此位置不属于已注册的旅店排名，已停止卸载。$\r$\n$2" /SD IDOK
    SetErrorLevel 2
    Abort
  safe:
  StrCpy $0 '--shutdown'
  Call un.Guard
  StrCmp $1 "0" stopped
    SetErrorLevel 2
    Abort
  stopped:
  Sleep 500
  ClearErrors
  !insertmacro DeletePayload "HearthstoneRank.exe"
  !insertmacro DeletePayload "HearthstoneRank.exe.config"
  !insertmacro DeletePayload "LICENSE.txt"
  !insertmacro DeletePayload "HearthstoneRank-README.md"
  ; A locked payload keeps its uninstaller, marker and registration available.
  Delete "$INSTDIR\HearthstoneRank.install"
  IfErrors uninstall_failed
  Delete "$SMPROGRAMS\旅店排名\旅店排名.lnk"
  Delete "$SMPROGRAMS\旅店排名\设置.lnk"
  Delete "$SMPROGRAMS\旅店排名\卸载.lnk"
  RMDir "$SMPROGRAMS\旅店排名"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HearthstoneRank"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Goto uninstall_done
  uninstall_failed:
    DetailPrint "文件仍被占用或不能删除；保留卸载入口，请关闭程序后重试。"
    MessageBox MB_OK|MB_ICONEXCLAMATION "部分文件不能删除。卸载入口已保留，请关闭程序后重试。" /SD IDOK
    SetErrorLevel 2
    Abort
  uninstall_done:
SectionEnd
