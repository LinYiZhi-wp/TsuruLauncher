#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
一键发版：干净发布 -> 泄露审计 -> 打安装器 -> 打便携版 -> 再审计

用法（在 Setup/ 目录下，版本号会自动从 csproj 读）：
    python build-release.py

为什么要有这个脚本：
  · 手动打包容易漏步骤（忘了删 publish/ 旧文件、忘了跑审计）
  · **账号数据泄露是零容忍的** —— 所以审计跑两遍：
    打包前查输入（publish/），打包后查产物（zip 解压后逐文件）

退出码：0 = 成功且审计通过，非 0 = 失败（不会产出发布包）
"""

import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CSPROJ = os.path.join(REPO, 'TsuruLauncher.csproj')
PUBLISH = os.path.join(REPO, 'publish')
OUTPUT = os.path.join(HERE, 'Output')
ISCC = os.path.join(os.environ.get('LOCALAPPDATA', ''),
                    'Programs', 'Inno Setup 6', 'ISCC.exe')


def run(cmd, cwd=None, shell=False):
    print(f'  $ {cmd if isinstance(cmd, str) else " ".join(cmd)}')
    r = subprocess.run(cmd, cwd=cwd, shell=shell,
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    if r.returncode != 0:
        print(r.stdout[-2000:])
        print(r.stderr[-2000:])
        raise SystemExit(f'❌ 命令失败：{cmd}')
    return r.stdout


def version():
    s = open(CSPROJ, encoding='utf-8').read()
    m = re.search(r'<Version>([^<]+)</Version>', s)
    return m.group(1) if m else '0.0.0'


def audit(stage):
    print(f'\n{"=" * 62}\n  泄露审计（{stage}）\n{"=" * 62}')
    r = subprocess.run([sys.executable, 'audit-package.py'], cwd=HERE)
    if r.returncode != 0:
        raise SystemExit('❌ 审计未通过 —— 已中止，不会产出发布包')


def main():
    v = version()
    print(f'发布版本：v{v}')

    # ── 1. 干净发布 ─────────────────────────────────────────────
    # ⚠⚠ 必须先删 publish/！`dotnet publish -o` 复用旧目录时**不会清理**
    #    上一次的残留（历史上就因此把开发机的 config.json 打进了发布包）。
    print(f'\n[1/5] 干净发布（先删 publish/）')
    if os.path.isdir(PUBLISH):
        import shutil
        shutil.rmtree(PUBLISH)
        print('  已删除旧的 publish/')
    run(['dotnet', 'publish', 'TsuruLauncher.csproj', '-c', 'Release',
         '-o', 'publish', '--no-restore', '-p:DebugType=none'], cwd=REPO)

    # 顺带确认没有配置泄漏到 publish
    for bad in ('config.json', 'accounts.json'):
        if os.path.exists(os.path.join(PUBLISH, bad)):
            raise SystemExit(f'❌ publish/{bad} 存在 —— 会泄露！已中止。')

    # ── 2. 审计输入 ─────────────────────────────────────────────
    audit('打包前 / 输入 publish')

    # ── 3. 安装器 ───────────────────────────────────────────────
    print(f'\n[3/5] 编译安装器')
    if not os.path.isfile(ISCC):
        raise SystemExit(f'❌ 找不到 Inno Setup：{ISCC}')
    if os.path.isdir(OUTPUT):
        import shutil
        shutil.rmtree(OUTPUT)
    run([ISCC, 'Tsuru.iss'], cwd=HERE)

    # ── 4. 便携版 zip ───────────────────────────────────────────
    print(f'\n[4/5] 打包便携版 zip')
    zip_path = os.path.join(OUTPUT, f'Tsuru-v{v}.zip')
    import zipfile
    with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for root, _, files in os.walk(PUBLISH):
            for f in files:
                fp = os.path.join(root, f)
                z.write(fp, os.path.relpath(fp, PUBLISH).replace('\\', '/'))
    print(f'  -> {zip_path}')

    # ── 5. 再审计一次产物 ───────────────────────────────────────
    audit('打包后 / 产物')

    print(f'\n{"=" * 62}')
    print('✅ 全部完成，产物：')
    for f in sorted(os.listdir(OUTPUT)):
        fp = os.path.join(OUTPUT, f)
        print(f'   {f}  ({os.path.getsize(fp) / 1048576:.2f} MB)')
    print('=' * 62)


if __name__ == '__main__':
    main()
