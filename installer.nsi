!include "MUI2.nsh"

Name "Scarl Professional"
OutFile "Scarl_Installer.exe"
InstallDir "$PROGRAMFILES64\Scarl"
RequestExecutionLevel admin

!define MUI_ABORTWARNING
!define MUI_ICON "Scarl.UI\Assets\logo.ico"
!define MUI_WELCOMEFINISHEDPAGE_NOREBOOT

; Welcome Page Slogan
!define MUI_WELCOMEPAGE_TITLE "Scarl Professional - ITS RAZE SO ITS CRAZE"
!define MUI_WELCOMEPAGE_TEXT "Welcome to Scarl, the next-gen image reconstruction suite.$\r$\n$\r$\nNOTE: Scarl uses high-performance Machine Learning (ML) engines and DirectML. Some Antivirus software may flag the core AI engine (scarl_core.dll) as a false positive. Scarl is 100% safe and ad-free."

; Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "MainSection" SEC01
    SetOutPath "$INSTDIR"
    File "Scarl.UI\bin\Release\net11.0-windows\Scarl.UI.exe"
    File "Scarl.UI\bin\Release\net11.0-windows\Scarl.UI.dll"
    File "Scarl.UI\bin\Release\net11.0-windows\Scarl.UI.runtimeconfig.json"
    File "Scarl.UI\bin\Release\net11.0-windows\scarl_core.dll"
    File "Scarl.UI\bin\Release\net11.0-windows\DirectML.dll"
    
    ; Create Models Directory and copy all assets
    SetOutPath "$INSTDIR\models"
    File "models\characters.txt"
    File "models\classifier.onnx"
    File "models\clip_merges.txt"
    File "models\clip_text.onnx"
    File "models\clip_vision.onnx"
    File "models\clip_vocab.json"
    File "models\hat-x4.onnx"
    File "models\imagenet_labels.txt"
    File "models\RealESRGAN_x2_fp16.onnx"
    File "models\RealESRGAN_x4.onnx"
    File "models\RealESRGAN_x8_fp16.onnx"
    File "models\realesrgan-x2.onnx"
    File "models\realesrgan-x4.onnx"
    File "models\realesrgan-x8.onnx"
    
    SetOutPath "$INSTDIR"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    
    ; Shortcuts
    CreateShortcut "$DESKTOP\Scarl.lnk" "$INSTDIR\Scarl.UI.exe"
SectionEnd

Section "Uninstall"
    Delete "$DESKTOP\Scarl.lnk"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir /r "$INSTDIR"
SectionEnd
