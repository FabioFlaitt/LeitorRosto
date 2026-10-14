#!/usr/bin/env python3
"""
Script para verificar e instalar dependências necessárias para face recognition
"""

import subprocess
import sys

def check_and_install():
    packages = [
        'face_recognition',
        'opencv-python',
        'ultralytics'
    ]
    
    print("=" * 60)
    print("Verificando dependências Python para Face Recognition")
    print("=" * 60)
    print()
    
    missing_packages = []
    
    for package in packages:
        try:
            __import__(package.replace('-', '_'))
            print(f"✓ {package:<30} OK")
        except ImportError:
            print(f"✗ {package:<30} NÃO INSTALADO")
            missing_packages.append(package)
    
    print()
    
    if not missing_packages:
        print("✓ Todas as dependências estão instaladas!")
        return True
    
    print(f"Faltam {len(missing_packages)} pacote(s):")
    for pkg in missing_packages:
        print(f"  - {pkg}")
    
    print()
    response = input("Deseja instalar os pacotes faltantes? (s/n): ").strip().lower()
    
    if response != 's':
        print("Instalação cancelada.")
        return False
    
    print()
    print("Instalando pacotes...")
    for package in missing_packages:
        print(f"  Instalando {package}...", end=' ')
        try:
            subprocess.check_call([sys.executable, '-m', 'pip', 'install', package])
            print("✓")
        except subprocess.CalledProcessError:
            print("✗ ERRO")
            return False
    
    print()
    print("=" * 60)
    print("✓ Todas as dependências foram instaladas com sucesso!")
    print("=" * 60)
    return True

if __name__ == '__main__':
    success = check_and_install()
    sys.exit(0 if success else 1)
