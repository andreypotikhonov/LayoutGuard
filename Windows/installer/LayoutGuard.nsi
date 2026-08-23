Unicode True
SetCompressor /SOLID lzma

!define APP_NAME "LayoutGuard"
!define APP_VERSION "0.2.4"
!define APP_EXE "LayoutGuard.exe"

Name "${APP_NAME}"
OutFile "..\artifacts\installer\LayoutGuard-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
RequestExecutionLevel user
BrandingText "LayoutGuard"

Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Section "LayoutGuard" SEC_MAIN
  SetShellVarContext current
  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /IM "${APP_EXE}" /F'
  Sleep 300
  SetOutPath "$INSTDIR"
  File /r "..\artifacts\publish\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\LayoutGuard"
  CreateShortcut "$SMPROGRAMS\LayoutGuard\LayoutGuard.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$SMPROGRAMS\LayoutGuard\Удалить LayoutGuard.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LayoutGuard" "DisplayName" "LayoutGuard"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LayoutGuard" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LayoutGuard" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LayoutGuard" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  Exec "$INSTDIR\${APP_EXE}"
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /IM "${APP_EXE}" /F'
  Sleep 300
  Delete "$SMPROGRAMS\LayoutGuard\LayoutGuard.lnk"
  Delete "$SMPROGRAMS\LayoutGuard\Удалить LayoutGuard.lnk"
  RMDir "$SMPROGRAMS\LayoutGuard"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LayoutGuard"
  RMDir /r "$INSTDIR"
SectionEnd
