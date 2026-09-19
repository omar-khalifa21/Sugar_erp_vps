Unicode true
SetCompressor /SOLID zlib
!include "MUI2.nsh"
!include "FileFunc.nsh"
!define PRODUCT_KEY "Kitchen"
!ifndef SHARED_INCLUDE_DIR
  !define SHARED_INCLUDE_DIR "${__FILEDIR__}\..\shared"
!endif
!include "${SHARED_INCLUDE_DIR}\LocalSettings.nsh"
!ifndef PUBLISH_DIR
  !error "PUBLISH_DIR required"
!endif
Name "Sugar ERP - Kitchen"
OutFile "${OUTPUT_DIR}\Sugar-Kitchen-${APP_VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\Sugar ERP\Kitchen"
RequestExecutionLevel user

Icon "..\..\apps\kitchen\Assets\sugar.ico"
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "Sugar ERP Kitchen"
VIAddVersionKey "FileDescription" "Sugar ERP Kitchen desktop PC installer"
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
  CreateShortCut "$SMPROGRAMS\Sugar ERP\Kitchen.lnk" "$INSTDIR\SugarERP.Kitchen.Desktop.exe"
  ReadRegDWORD $ShortcutChoice HKCU "Software\Sugar ERP\${PRODUCT_KEY}" "DesktopShortcut"
  ${If} $ShortcutChoice == 1
    CreateShortCut "$DESKTOP\Sugar ERP Kitchen.lnk" "$INSTDIR\SugarERP.Kitchen.Desktop.exe"
  ${Else}
    Delete "$DESKTOP\Sugar ERP Kitchen.lnk"
  ${EndIf}
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Kitchen" "DisplayName" "Sugar ERP Kitchen"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Kitchen" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Kitchen" "Publisher" "Sugar"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Kitchen" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/Relaunch" $1
  ${IfNot} ${Errors}
    Exec '"$INSTDIR\SugarERP.Kitchen.Desktop.exe"'
  ${EndIf}
SectionEnd
Section "Uninstall"
  Delete "$DESKTOP\Sugar ERP Kitchen.lnk"
  Delete "$DESKTOP\Sugar Kitchen.lnk"
  Delete "$SMPROGRAMS\Sugar ERP\Kitchen.lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SugarERP.Kitchen"
SectionEnd
