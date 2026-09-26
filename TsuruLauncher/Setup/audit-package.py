#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
打包前 / 打包后审计：确认**用户的账号数据没有被打进发布包**。

用法（在 Setup/ 目录下）：
    python audit-package.py

检查三样东西：
  1. publish/           —— 会被打进安装器和 zip 的原始文件
  2. Setup/Output/*.exe —— 安装器
  3. Setup/Output/*.zip —— 便携版

判定依据（从本机 %APPDATA% 的真实配置里读）：
  · 账号 UUID
  · 账号用户名
  · access / refresh / minecraft token
  · 皮肤文件的 SHA1（用户自己那张，不该出现在包里的默认皮肤之外）

退出码：0 = 干净，1 = 发现泄露。
"""

import hashlib
import json
import os
import re
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
PUBLISH = os.path.join(REPO, 'publish')
OUTPUT = os.path.join(HERE, 'Output')
def _appdata():
    """
    拿 %APPDATA%。⚠ Git Bash 里这个环境变量可能没有，
    所以要退回 ~/AppData/Roaming —— 否则比对基准是空的，审计等于没做。
    """
    v = os.environ.get('APPDATA')
    if v and os.path.isdir(v):
        return v
    guess = os.path.join(os.path.expanduser('~'), 'AppData', 'Roaming')
    return guess if os.path.isdir(guess) else ''


APPDATA = _appdata()
CONFIG = os.path.join(APPDATA, 'TsuruLauncher', 'config.json')
SKIN_DIR = os.path.join(APPDATA, 'TsuruLauncher', 'skins', '.account')


def load_secrets():
    """从本机真实配置里提取需要确保不泄露的内容。"""
    secrets = set()

    if os.path.isfile(CONFIG):
        try:
            cfg = json.load(open(CONFIG, encoding='utf-8'))
        except Exception:
            cfg = {}

        accounts = list(cfg.get('Accounts') or [])
        if cfg.get('SelectedAccount'):
            accounts.append(cfg['SelectedAccount'])

        for a in accounts:
            if not isinstance(a, dict):
                continue
            for k in ('Uuid', 'AccessToken', 'RefreshToken', 'MinecraftAccessToken',
                      'ClientToken', 'YggdrasilServer'):
                v = a.get(k)
                if isinstance(v, str) and len(v) >= 8:
                    secrets.add(v)
            # 用户名单独处理（可能很短，单独判断）
        # 用户名单独收集
        names = {a.get('Username') for a in accounts
                 if isinstance(a, dict) and isinstance(a.get('Username'), str)
                 and len(a.get('Username') or '') >= 4}
    else:
        names = set()

    # 用户自己的皮肤文件 SHA1
    skin_hashes = set()
    if os.path.isdir(SKIN_DIR):
        for root, _, files in os.walk(SKIN_DIR):
            for f in files:
                if not f.lower().endswith('.png'):
                    continue
                try:
                    skin_hashes.add(hashlib.sha1(
                        open(os.path.join(root, f), 'rb').read()).hexdigest())
                except Exception:
                    pass

    return secrets, names, skin_hashes


TOKEN_PATTERNS = [
    (rb'eyJ[A-Za-z0-9_\-]{20,}\.[A-Za-z0-9_\-]{20,}', 'JWT (eyJ...)'),
    (rb'M\.C5[0-9]{5}\.[A-Za-z0-9_\-]{20,}', 'Microsoft 令牌'),
]


def scan_bytes(name, data, secrets, names, skin_hashes, hits):
    """
    扫一段字节，命中就记进 hits。

    ⚠ 必须同时按 **UTF-8 和 UTF-16LE** 两种编码搜：
      .NET 程序集里的字符串常量是 UTF-8，但 Windows 的**版本信息资源是 UTF-16** ——
      只搜 UTF-8 会漏掉安装器里的 AppPublisherURL 这类字段（假阴性）。
    """
    def encodings(s):
        return (s.encode('utf-8'), s.encode('utf-16-le'))

    for s in secrets:
        for enc in encodings(s):
            if enc in data:
                hits.append((name, f'账号数据: {s[:8]}...'))
                break
    for n in names:
        for enc in encodings(n):
            if enc in data:
                hits.append((name, f'用户名: {n}'))
                break
    for pat, label in TOKEN_PATTERNS:
        if re.search(pat, data) or re.search(pat.replace(b'(', b'(?:').replace(b'.', b'.'), data):
            hits.append((name, f'令牌特征: {label}'))


def scan_dir(path, secrets, names, skin_hashes, hits):
    count = 0
    for root, _, files in os.walk(path):
        for f in files:
            fp = os.path.join(root, f)
            rel = os.path.relpath(fp, REPO)
            try:
                data = open(fp, 'rb').read()
            except Exception:
                continue
            count += 1
            scan_bytes(rel, data, secrets, names, skin_hashes, hits)

            # 皮肤哈希比对（只对 png）
            if f.lower().endswith('.png'):
                h = hashlib.sha1(data).hexdigest()
                if h in skin_hashes:
                    hits.append((rel, '用户自己的皮肤文件'))
    return count


def scan_zip(path, secrets, names, skin_hashes, hits):
    try:
        z = zipfile.ZipFile(path)
    except Exception as e:
        hits.append((path, f'打不开 zip: {e}'))
        return 0
    for n in z.namelist():
        try:
            data = z.read(n)
        except Exception:
            continue
        scan_bytes(f'{os.path.basename(path)}!{n}', data, secrets, names, skin_hashes, hits)
        if n.lower().endswith('.png') and hashlib.sha1(data).hexdigest() in skin_hashes:
            hits.append((f'{os.path.basename(path)}!{n}', '用户自己的皮肤文件'))
    return len(z.namelist())


def main():
    print('=' * 62)
    print(' 打包泄露审计 —— 确认账号数据没被打进发布包')
    print('=' * 62)

    if not os.path.isfile(CONFIG):
        print(f'⚠️  找不到本机配置：{CONFIG}')
        print('   （没有账号数据可比对，只做令牌特征扫描）')
    secrets, names, skin_hashes = load_secrets()
    print(f'\n比对基准：{len(secrets)} 条账号数据、{len(names)} 个用户名、'
          f'{len(skin_hashes)} 张自有皮肤')

    hits = []

    print('\n[1/3] publish/')
    if os.path.isdir(PUBLISH):
        n = scan_dir(PUBLISH, secrets, names, skin_hashes, hits)
        print(f'      扫了 {n} 个文件')
    else:
        print('      （不存在，跳过）')

    print('\n[2/3] Setup/Output/*.exe')
    if os.path.isdir(OUTPUT):
        for f in os.listdir(OUTPUT):
            if not f.lower().endswith('.exe'):
                continue
            fp = os.path.join(OUTPUT, f)
            data = open(fp, 'rb').read()

            # ⚠⚠ 安装器是 Inno Setup 打的，载荷用 **lzma2 压缩** ——
            #   直接裸扫字节**什么都扫不到**（实测连 TsuruLauncher / github.com
            #   这种必然存在的字符串都找不到），得到的"干净"是**假阴性**。
            #   所以这里只做一层"压缩包特征"检测，真正的判据是上面的 publish/
            #   （安装器的内容 100% 来自 publish/，只要 publish 干净，装出来就干净）。
            found = scan_bytes(f, data, secrets, names, skin_hashes, hits)
            print(f'      扫了 {f}（{len(data) / 1048576:.2f} MB）'
                  f' —— ⚠ 载荷是压缩的，此结果仅供参考')
            print('        真正的判据是 publish/：安装器内容全部来自它')

        print('\n[3/3] Setup/Output/*.zip')
        for f in os.listdir(OUTPUT):
            if f.lower().endswith('.zip'):
                n = scan_zip(os.path.join(OUTPUT, f), secrets, names, skin_hashes, hits)
                print(f'      扫了 {f} 里 {n} 个条目')
    else:
        print('      （不存在，跳过）')

    # ── 阳性对照：证明扫描器不是"永远说干净"的摆设 ──────────────
    print('\n[自检] 阳性对照 —— 扫描器真的能找到东西吗')
    if secrets:
        probe = next(iter(secrets))
        fake = (probe.encode('utf-8') + b'xxxx')
        tmp = []
        scan_bytes('<对照>', fake, secrets, set(), set(), tmp)
        ok = len(tmp) > 0
        print(f'      注入一段已知账号数据 -> {"✓ 检出" if ok else "❌ 漏检！"}')
        if not ok:
            hits.append(('<阳性对照>', '扫描器失效，本次审计结论不可信'))
    else:
        print('      ⚠ 没读到本机账号数据，无法做对照（审计覆盖不完整）')
        hits.append(('<审计覆盖>', '缺少比对基准，结论不完整'))

    print('\n' + '=' * 62)
    if hits:
        print('❌ 发现泄露！')
        for where, what in hits:
            print(f'   · {where}  ->  {what}')
        print('=' * 62)
        return 1

    print('✅ 干净：发布包里没有任何账号数据 / 令牌 / 自有皮肤')
    print('=' * 62)
    return 0


if __name__ == '__main__':
    sys.exit(main())
