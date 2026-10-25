; SatraN Windows installer (NSIS, built on Linux by package.py with makensis)
Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma
!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!ifndef SRC
  !define SRC "SatraN"
!endif
!define APP "SatraN"
!define UNKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\SatraN"
!define SVC "SatraNHelper"

Name "${APP} ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\${APP}"
InstallDirRegKey HKLM "${UNKEY}" "InstallLocation"
RequestExecutionLevel admin
BrandingText "${APP} ${VERSION}"
!ifndef NUMVER
  !define NUMVER "${VERSION}"
!endif
VIProductVersion "${NUMVER}.0"
VIAddVersionKey "ProductName" "${APP}"
VIAddVersionKey "CompanyName" "Satra"
VIAddVersionKey "FileDescription" "${APP} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Satra"

!define MUI_ICON "satran.ico"
!define MUI_UNICON "satran.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${APP}"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApp

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_COMPONENTS
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

; Launch unelevated (as the desktop user) via explorer
Function LaunchApp
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\SatraN.exe"'
FunctionEnd

!macro StopAll
  nsExec::Exec 'sc.exe stop ${SVC}'
  nsExec::Exec 'taskkill /F /T /IM SatraN.exe'
  nsExec::Exec 'taskkill /F /T /IM sing-box.exe'
  nsExec::Exec 'taskkill /F /T /IM xray.exe'
  Sleep 1200
!macroend

!macro MoveLegacy D
  ${If} ${FileExists} "$INSTDIR\${D}\*.*"
  ${AndIfNot} ${FileExists} "$INSTDIR\legacy-data\${D}\*.*"
    CreateDirectory "$INSTDIR\legacy-data"
    Rename "$INSTDIR\${D}" "$INSTDIR\legacy-data\${D}"
  ${EndIf}
!macroend

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "${APP} needs 64-bit Windows 10 or 11."
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext all
FunctionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext all
FunctionEnd

Section "!${APP}" SecMain
  SectionIn RO
  !insertmacro StopAll
  nsExec::Exec 'sc.exe delete ${SVC}'
  ; upgrade from <=1.7.2 (data stored next to the exe): keep it for one-time migration to %LOCALAPPDATA%
  !insertmacro MoveLegacy guiConfigs
  !insertmacro MoveLegacy guiBackups
  !insertmacro MoveLegacy binConfigs
  !insertmacro MoveLegacy rollback
  ; remove previous program files (keep legacy-data) so no stale DLLs remain
  RMDir /r "$INSTDIR\bin"
  Delete "$INSTDIR\*.*"
  FindFirst $0 $1 "$INSTDIR\*"
  loop:
    StrCmp $1 "" done
    StrCmp $1 "." next
    StrCmp $1 ".." next
    StrCmp $1 "legacy-data" next
    RMDir /r "$INSTDIR\$1"
  next:
    FindNext $0 $1
    Goto loop
  done:
  FindClose $0
  ; old script-based installer leftovers
  Delete "$SMPROGRAMS\Uninstall SatraN.lnk"
  RMDir /r "$INSTDIR.old"
  RMDir /r "$INSTDIR.new"

  SetOutPath "$INSTDIR"
  File /r "${SRC}\*.*"
  File "satran.ico"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; SatraN Helper: LocalSystem service + named pipe -> TUN without UAC on connect
  nsExec::ExecToLog '"$INSTDIR\SatraN.exe" --helper-install'
  Pop $0
  DetailPrint "SatraN Helper service: exit $0"

  CreateShortcut "$SMPROGRAMS\${APP}.lnk" "$INSTDIR\SatraN.exe" "" "$INSTDIR\satran.ico"

  WriteRegStr HKLM "${UNKEY}" "DisplayName" "${APP}"
  WriteRegStr HKLM "${UNKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNKEY}" "Publisher" "Satra"
  WriteRegStr HKLM "${UNKEY}" "DisplayIcon" "$INSTDIR\satran.ico"
  WriteRegStr HKLM "${UNKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNKEY}" "URLInfoAbout" "https://github.com/gitsatra/SatraN"
  WriteRegStr HKLM "${UNKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${UNKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "${UNKEY}" "EstimatedSize" "$0"

  ; silent mode (in-app updater): start the new version for the user
  IfSilent 0 +2
    Call LaunchApp
SectionEnd

Section "Desktop shortcut" SecDesktop
  CreateShortcut "$DESKTOP\${APP}.lnk" "$INSTDIR\SatraN.exe" "" "$INSTDIR\satran.ico"
SectionEnd

Section "Uninstall"
  !insertmacro StopAll
  nsExec::Exec '"$INSTDIR\SatraN.exe" --helper-uninstall'
  nsExec::Exec 'sc.exe delete ${SVC}'
  ; autostart entry the app may have written for the user
  nsExec::Exec `powershell -NoProfile -ExecutionPolicy Bypass -Command "$$k='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'; (Get-Item $$k).Property | ? { $$_ -like 'v2rayNAutoRun_*' } | % { Remove-ItemProperty $$k $$_ }; Get-ScheduledTask -TaskName 'v2rayNAutoRun_*' -EA 0 | Unregister-ScheduledTask -Confirm:$$false"`
  Delete "$SMPROGRAMS\${APP}.lnk"
  Delete "$SMPROGRAMS\Uninstall SatraN.lnk"
  Delete "$DESKTOP\${APP}.lnk"
  SetShellVarContext current
  Delete "$DESKTOP\${APP}.lnk"
  SetShellVarContext all
  RMDir /r "$INSTDIR"
  ; ProgramData\SatraN (helper run dir + log); with context=all $APPDATA is ProgramData
  RMDir /r "$APPDATA\SatraN"
  RMDir /r "$TEMP\SatraN-update"
  DeleteRegKey HKLM "${UNKEY}"
SectionEnd

Section /o "un.Delete my servers, subscriptions and settings" SecUnData
  SetShellVarContext current
  RMDir /r "$LOCALAPPDATA\SatraN"
  RMDir /r "$TEMP\SatraN-update"
SectionEnd
