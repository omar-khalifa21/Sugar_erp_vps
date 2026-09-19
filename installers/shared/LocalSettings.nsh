!include "nsDialogs.nsh"
!include "LogicLib.nsh"
Var ReportsDirectory
Var ReportsInput
Var ShortcutCheckbox
Var SettingsDialog
Var ReportsProbe
Var ShortcutChoice

Function LocalSettingsPage
  ReadRegStr $ReportsDirectory HKCU "Software\Sugar ERP\${PRODUCT_KEY}" "ReportsDirectory"
  ${If} $ReportsDirectory == ""
    StrCpy $ReportsDirectory "$DOCUMENTS\Sugar ERP\${PRODUCT_KEY}\Reports"
  ${EndIf}
  nsDialogs::Create 1018
  Pop $SettingsDialog
  ${NSD_CreateLabel} 0 0 100% 24u "Excel & Reports Location (preserved during upgrades and uninstall)"
  Pop $0
  ${NSD_CreateDirRequest} 0 30u 78% 14u "$ReportsDirectory"
  Pop $ReportsInput
  ${NSD_CreateBrowseButton} 80% 30u 20% 14u "Browse..."
  Pop $0
  ${NSD_OnClick} $0 BrowseReports
  ReadRegDWORD $ShortcutChoice HKCU "Software\Sugar ERP\${PRODUCT_KEY}" "DesktopShortcut"
  ${NSD_CreateCheckbox} 0 64u 100% 14u "Create a desktop shortcut"
  Pop $ShortcutCheckbox
  ${If} $ShortcutChoice == ""
    ${NSD_Check} $ShortcutCheckbox
  ${ElseIf} $ShortcutChoice == 1
    ${NSD_Check} $ShortcutCheckbox
  ${EndIf}
  nsDialogs::Show
FunctionEnd

Function BrowseReports
  nsDialogs::SelectFolderDialog "Select Excel/report directory" "$ReportsDirectory"
  Pop $0
  ${If} $0 != "error"
    ${NSD_SetText} $ReportsInput $0
  ${EndIf}
FunctionEnd

Function LocalSettingsLeave
  ${NSD_GetText} $ReportsInput $ReportsDirectory
  ${If} $ReportsDirectory == ""
    MessageBox MB_ICONEXCLAMATION "Choose a report directory."
    Abort
  ${EndIf}
  CreateDirectory "$ReportsDirectory"
  ClearErrors
  GetTempFileName $ReportsProbe "$ReportsDirectory"
  ${If} ${Errors}
    MessageBox MB_ICONEXCLAMATION "This report directory is not writable. Choose another directory."
    Abort
  ${EndIf}
  FileOpen $0 "$ReportsProbe" w
  ${If} ${Errors}
    MessageBox MB_ICONEXCLAMATION "This report directory is not writable. Choose another directory."
    Abort
  ${EndIf}
  FileClose $0
  Delete "$ReportsProbe"
  WriteRegStr HKCU "Software\Sugar ERP\${PRODUCT_KEY}" "ReportsDirectory" "$ReportsDirectory"
  ${NSD_GetState} $ShortcutCheckbox $ShortcutChoice
  WriteRegDWORD HKCU "Software\Sugar ERP\${PRODUCT_KEY}" "DesktopShortcut" $ShortcutChoice
FunctionEnd
