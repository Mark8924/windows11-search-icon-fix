#!/usr/bin/env python3
"""Rebuild bundled native payloads using llvm-mingw. No third-party Python modules."""
import argparse
import gzip
from pathlib import Path
import subprocess
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('--toolchain', required=True, type=Path, help='llvm-mingw installation directory')
args = parser.parse_args()
root = Path(__file__).resolve().parent
with tempfile.TemporaryDirectory() as temp:
    for arch, triple in [('arm64', 'aarch64'), ('x64', 'x86_64')]:
        compiler = args.toolchain.resolve() / 'bin' / (triple + '-w64-mingw32-clang++')
        for name, options in [('SearchAnimationBridge.dll', ['-shared']), ('AnimationBridgeHost.exe', ['-municode', '-mwindows', '-DBRIDGE_HOST'])]:
            output = Path(temp) / name
            subprocess.run([str(compiler), '-std=c++17', '-O2', '-static', *options,
                            str(root / 'AnimationBridge.cpp'), '-o', str(output),
                            '-lole32', '-loleaut32', '-lruntimeobject', '-luuid', '-lshell32',
                            '-Wno-dll-attribute-on-redeclaration'], check=True)
            payload = root / ('Native.' + arch + '.' + name + '.gz')
            payload.write_bytes(gzip.compress(output.read_bytes(), mtime=0))
            print(payload.name, payload.stat().st_size)
