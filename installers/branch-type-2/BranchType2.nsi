Unicode true
SetCompressor /SOLID zlib
!include "MUI2.nsh"
!include "FileFunc.nsh"
!define PRODUCT_KEY "Branch2"
!ifndef SHARED_INCLUDE_DIR
  !define SHARED_INCLUDE_DIR "${__FILEDIR__}\..\shared"
!endif
!include "${SHARED_INCLUDE_DIR}/LocalSettings.nsh"
!ifndef PUBLISH_DIR
  !error "PUBLISH_DIR required"
!endif
Name "Sugar ERP - Branch2"
OutFile "${OUTPUT_DIR}\Sugar-Branch2-${APP_VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\Sugar ERP\Branch2"
RequestExecutionLevel user

Icon "..\..\apps\branch-type-2\Assets\sugar.ico"
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "Sugar ERP Branch2"
VIAddVersionKey "FileDescription" "Sugar ERP Branch2 desktop installer"
VIAddVersionKey "CompanyName" "Sugar"
VIAddVersionKey "LegalCopyright" "Copyright (c) Sugar"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
!insertmacro MUI_PAGE_DIRECTORY
Page custom LocalSettingsPage LocalSettingsLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
Section "Install"
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\Sugar ERP"
  CreateShortCut "$SMPROGRAMS\Sugar ERP\Branch2.lnk" "$INSTDIR\SugarERP.Branch2.Desktop.exe"
  ReadRegDWORD $ShortcutChoice HKCU "Software\Sugar ERP\${PRODUCT_KEY}" "DesktopShortcut"
  ${If} $ShortcutChoice == 1
    CreateShortCut "$DESKTOP\Sugar ERP Branch2.lnk" "$INSTDIR\SugarERP.Branch2.Desktop.exe"
  ${Else}
    Delete "$DESKTOP\Sugar ERP Branch2.lnk"
  ${EndIf}
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Branch2" "DisplayName" "Sugar ERP Branch2"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Branch2" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Branch2" "Publisher" "Sugar"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Branch2" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/Relaunch" $1
  ${IfNot} ${Errors}
    Exec '"$INSTDIR\SugarERP.Branch2.Desktop.exe"'
  ${EndIf}
SectionEnd
Section "Uninstall"
  Delete "$DESKTOP\Sugar ERP Branch2.lnk"
  Delete "$DESKTOP\Sugar Branch2.lnk"
  Delete "$SMPROGRAMS\Sugar ERP\Branch2.lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Branch2"
SectionEnd
