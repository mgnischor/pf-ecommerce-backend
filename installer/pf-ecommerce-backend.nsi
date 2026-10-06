; pf-ecommerce-backend Windows installer (NSIS 3, Unicode).
;
; Installs the self-contained win-x64 publish output of Portfolio.csproj
; (the .NET runtime travels inside the binaries, so no .NET installation
; is required on the target machine), registers Add/Remove Programs
; entries, and creates Start Menu shortcuts.
;
; Build the publish output first, then compile this script:
;   dotnet publish Portfolio.csproj -c Release -r win-x64 --self-contained ^
;     -p:PublishSingleFile=true -o dist\win-x64
;   makensis /DVERSION=1.2.3 installer\pf-ecommerce-backend.nsi
;
; Overridable definitions (/DNAME=value):
;   VERSION      version stamped into the installer name and the registry (default 0.0.0-dev)
;   PUBLISH_DIR  publish output to pack (default ..\dist\win-x64, relative to this script)
;   OUTFILE      installer path (default ..\dist\pf-ecommerce-backend-<VERSION>-win-x64-setup.exe)

!ifndef VERSION
  !define VERSION "0.0.0-dev"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\dist\win-x64"
!endif
!ifndef OUTFILE
  !define OUTFILE "..\dist\pf-ecommerce-backend-${VERSION}-win-x64-setup.exe"
!endif

!define APP_NAME "pf-ecommerce-backend"
!define APP_PUBLISHER "pf-ecommerce"
!define APP_EXECUTABLE "Portfolio.exe"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"

; --------------------------------- General ---------------------------------

Name "${APP_NAME} ${VERSION}"
OutFile "${OUTFILE}"
Unicode True
RequestExecutionLevel admin
InstallDir "$PROGRAMFILES64\${APP_NAME}"
ShowInstDetails show
ShowUninstDetails show

!include "MUI2.nsh"

!define MUI_ABORTWARNING
!define MUI_ICON "${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico"
!define MUI_UNICON "${NSISDIR}\Contrib\Graphics\Icons\modern-uninstall.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE.md"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "English"

; --------------------------------- Install ---------------------------------

Section "Application files" SEC_APP
  SectionIn RO
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"

  WriteUninstaller "$INSTDIR\uninstall.exe"

  WriteRegStr HKLM "Software\${APP_NAME}" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "Software\${APP_NAME}" "Version" "${VERSION}"

  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME} ${VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKLM "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section "Start Menu shortcuts" SEC_SHORTCUTS
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXECUTABLE}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Uninstall.lnk" "$INSTDIR\uninstall.exe"
SectionEnd

LangString DESC_APP ${LANG_ENGLISH} "The API binaries and their runtime (required)."
LangString DESC_SHORTCUTS ${LANG_ENGLISH} "Start Menu entries for the API and the uninstaller."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_APP} $(DESC_APP)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_SHORTCUTS} $(DESC_SHORTCUTS)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; -------------------------------- Uninstall --------------------------------

Section "Uninstall"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  Delete "$INSTDIR\uninstall.exe"
  RMDir /r "$INSTDIR"

  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  DeleteRegKey HKLM "Software\${APP_NAME}"
SectionEnd
