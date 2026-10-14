@echo off
REM Script para instalar dependências Python para face recognition

echo ============================================
echo Instalando dependências Python...
echo ============================================
echo.

REM Verificar se pip está instalado
python -m pip --version >nul 2>&1
if errorlevel 1 (
    echo ERRO: Python/pip não encontrado no PATH
    echo Por favor, instale Python 3.8+ e adicione ao PATH
    pause
    exit /b 1
)

echo Atualizando pip...
python -m pip install --upgrade pip

echo.
echo Instalando face_recognition...
python -m pip install face_recognition

echo.
echo Instalando opencv-python...
python -m pip install opencv-python

echo.
echo Verificando instalação...
python -c "import face_recognition; import cv2; print('✓ Todos os pacotes instalados com sucesso!')"
if errorlevel 1 (
    echo ERRO: Falha na verificação de instalação
    pause
    exit /b 1
)

echo.
echo ============================================
echo ✓ Instalação concluída com sucesso!
echo ============================================
echo.
pause
