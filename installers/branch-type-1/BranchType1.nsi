Unicode true
SetCompressor /SOLID zlib
!include "MUI2.nsh"
!include "FileFunc.nsh"
!define PRODUCT_KEY "Branch1"
!ifndef SHARED_INCLUDE_DIR
  !define SHARED_INCLUDE_DIR "${__FILEDIR__}\..\shared"
!endif
!include "${SHARED_INCLUDE_DIR}/LocalSettings.nsh"
!ifndef PUBLISH_DIR
  !error "PUBLISH_DIR is required"
!endif
!ifndef OUTPUT_DIR
  !error "OUTPUT_DIR is required"
!endif
!ifndef APP_VERSION
  !error "APP_VERSION is required"
!endif
!ifndef VARIANT
  !error "VARIANT is required"
!endif

Name "Sugar ERP - Branch1 ${VARIANT}"
!if "${VARIANT}" == "Touch"
  OutFile "${OUTPUT_DIR}\Sugar-Branch-Type-1-Touch-${APP_VERSION}.exe"
!else
  OutFile "${OUTPUT_DIR}\Sugar-Branch-Type-1-${APP_VERSION}.exe"
!endif
InstallDir "$LOCALAPPDATA\Programs\Sugar ERP\Branch1 ${VARIANT}"
RequestExecutionLevel user

Icon "..\..\apps\branch-type-1\Assets\sugar.ico"
UninstallIcon "..\..\apps\branch-type-1\Assets\sugar.ico"
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "Sugar ERP Branch1 ${VARIANT}"
VIAddVersionKey "FileDescription" "Sugar ERP Branch1 ${VARIANT} setup"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "LegalCopyright" "Copyright (c) Sugar"

!insertmacro MUI_PAGE_DIRECTORY
Page custom LocalSettingsPage LocalSettingsLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "Install"
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*"
  !if "${VARIANT}" == "Touch"
    File "touch.variant"
  !endif
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\Sugar ERP"
  CreateShortCut "$SMPROGRAMS\Sugar ERP\Branch1 ${VARIANT}.lnk" "$INSTDIR\SugarERP.Branch1.exe"
  ReadRegDWORD $ShortcutChoice HKCU "Software\Sugar ERP\${PRODUCT_KEY}" "DesktopShortcut"
  ${If} $ShortcutChoice == 1
    CreateShortCut "$DESKTOP\Sugar ERP Branch1 ${VARIANT}.lnk" "$INSTDIR\SugarERP.Branch1.exe"
  ${Else}
    Delete "$DESKTOP\Sugar ERP Branch1 ${VARIANT}.lnk"
  ${EndIf}
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Branch1.${VARIANT}" "DisplayName" "Sugar ERP - Branch1 ${VARIANT}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Branch1.${VARIANT}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Branch1.${VARIANT}" "Publisher" "Sugar"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Branch1.${VARIANT}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Branch1.${VARIANT}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/Relaunch" $1
  ${IfNot} ${Errors}
    Exec '"$INSTDIR\SugarERP.Branch1.exe"'
  ${EndIf}
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\Sugar ERP Branch1 ${VARIANT}.lnk"
  Delete "$DESKTOP\Branch1 ${VARIANT}.lnk"
  Delete "$SMPROGRAMS\Sugar ERP\Branch1 ${VARIANT}.lnk"
  RMDir "$SMPROGRAMS\Sugar ERP"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Branch1.${VARIANT}"
  RMDir /r "$INSTDIR"
  ; Operational SQLite data lives outside INSTDIR and is deliberately preserved.
SectionEnd
