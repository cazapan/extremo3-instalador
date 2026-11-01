"""Organizer-only release preparation. Never downloads or redistributes the mod.
Python 3.11+, or Python 3.10 with: py -m pip install tomli
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import urllib.request
import zipfile
try:
    import tomllib
except ImportError:
    import tomli as tomllib

ROOT = Path(__file__).resolve().parents[1]

def numeric(value):
    if not re.fullmatch(r"\d+(?:\.\d+)*", value):
        raise ValueError(f"Versión no numérica no soportada: {value}")
    parts = tuple(map(int, value.split('.')))
    return parts + (0,) * (8-len(parts))

def contains(expression, version):
    """Conservative Maven interval subset; unknown/union syntax is rejected."""
    v = numeric(version)
    if re.fullmatch(r"\[\d+(?:\.\d+)*\]", expression):
        return v == numeric(expression[1:-1])
    m = re.fullmatch(r"([\[(])([0-9.]*),([0-9.]*)([\])])", expression)
    if not m:
        raise ValueError(f"Rango no soportado; revisar manualmente y extender el validador: {expression}")
    left, low, high, right = m.groups()
    return (not low or (v >= numeric(low) if left == '[' else v > numeric(low))) and \
           (not high or (v <= numeric(high) if right == ']' else v < numeric(high)))

def inspect_mod(data, neo):
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        entry = z.getinfo('META-INF/neoforge.mods.toml')
        if entry.file_size > 1_000_000:
            raise ValueError('Metadata demasiado grande')
        text = z.read(entry).decode('utf-8')
        meta = tomllib.loads(text)
        if meta.get('modLoader') != 'javafml':
            raise ValueError('Loader inesperado')
        mods = meta.get('mods', [])
        if len(mods) != 1 or mods[0].get('modId') != 'mcextremo':
            raise ValueError('Se esperaba únicamente el mod mcextremo')
        dependencies = meta.get('dependencies', {}).get('mcextremo', [])
        checked = []
        for d in dependencies:
            kind = d.get('type', 'required' if d.get('mandatory', True) else 'optional')
            if kind != 'required':
                raise ValueError(f'Dependencia con tipo {kind}: requiere revisión explícita')
            ident = d.get('modId')
            if ident not in {'minecraft', 'neoforge', 'java'}:
                raise ValueError(f'Dependencia externa sin resolver: {ident}. No se publicará el instalador.')
            version = {'minecraft': '1.21.1', 'neoforge': neo, 'java': '21'}[ident]
            if not contains(d.get('versionRange', ''), version):
                raise ValueError(f'{ident} {version} fuera de rango: {d}')
            checked.append(d)
        if not {'minecraft', 'neoforge'}.issubset({d['modId'] for d in checked}):
            raise ValueError('Faltan requisitos explícitos de Minecraft/NeoForge')
        return {'metadata': text, 'dependencies': checked, 'mod': mods[0],
                'licenseDeclaredInJar': meta.get('license'), 'loaderVersion': meta.get('loaderVersion')}

def get(url):
    with urllib.request.urlopen(url, timeout=90) as r:
        if not r.url.startswith('https://maven.neoforged.net/releases/'):
            raise ValueError('Redirección fuera del Maven oficial')
        return r.read(100_000_001)

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('jar', type=Path)
    p.add_argument('--mod-url', required=True, help='URL pública fija de un asset en GitHub Releases; tenés permiso del autor para alojarlo.')
    p.add_argument('--confirm-official-file-8401810', action='store_true', required=True,
                   help='Confirmás que descargaste este archivo concreto desde CurseForge, sin mirrors.')
    a = p.parse_args()
    if not re.fullmatch(r'https://github\.com/[^/]+/[^/]+/releases/download/[^/]+/[^/?#]+', a.mod_url):
        p.error('Usar URL fija https://github.com/USUARIO/REPO/releases/download/TAG/mcextremo-v14.jar')
    if a.jar.name != 'mcextremo-v14.jar':
        p.error('El archivo debe ser mcextremo-v14.jar, file ID 8401810')
    lock = json.loads((ROOT/'release-lock.json').read_text())
    report = inspect_mod(a.jar.read_bytes(), lock['neoForge'])
    class SafeRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, req, fp, code, msg, headers, newurl):
            from urllib.parse import urlparse
            u = urlparse(newurl)
            if u.scheme != 'https' or u.hostname not in {'github.com','release-assets.githubusercontent.com','objects.githubusercontent.com'}:
                raise ValueError('Redirección no permitida')
            return super().redirect_request(req, fp, code, msg, headers, newurl)
    with urllib.request.build_opener(SafeRedirect).open(a.mod_url, timeout=120) as response:
        hosted = response.read(200_000_001)
    if hashlib.sha256(hosted).digest() != hashlib.sha256(a.jar.read_bytes()).digest():
        raise ValueError('El archivo alojado no coincide con el JAR revisado')
    lock['modUrl'] = a.mod_url
    base = f"https://maven.neoforged.net/releases/net/neoforged/neoforge/{lock['neoForge']}/neoforge-{lock['neoForge']}-installer.jar"
    neo = get(base)
    official_hash = get(base+'.sha256').decode().strip().split()[0].lower()
    if hashlib.sha256(neo).hexdigest() != official_hash or official_hash != lock['neoSha256']:
        raise ValueError('El instalador no coincide con el hash fijado y publicado por NeoForged')
    with zipfile.ZipFile(io.BytesIO(neo)) as z:
        profile = json.loads(z.read('install_profile.json'))
        if profile['minecraft'] != '1.21.1' or profile['version'] != 'neoforge-'+lock['neoForge']:
            raise ValueError('Instalador para otra versión')
        # FML loaderVersion uses the major version of the bundled loader, not NeoForge's version.
        ver = json.loads(z.read('version.json'))
        args = ver['arguments']['game']
        fml = args[args.index('--fml.fmlVersion')+1]
        if not contains(report['loaderVersion'], fml):
            raise ValueError('FML fuera del loaderVersion requerido por el mod')
    lock['modSha256'] = hashlib.sha256(a.jar.read_bytes()).hexdigest()
    lock['reviewed'] = True
    report['officialFile'] = 'https://www.curseforge.com/minecraft/mc-mods/extreme-3-mechanics/files/8401810'
    report['trust'] = 'Descarga oficial manual confirmada por el organizador; hash SHA-256 calculado localmente. No es una firma del autor.'
    (ROOT/'release-audit.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    tmp = ROOT/'release-lock.json.tmp'
    tmp.write_text(json.dumps(lock, indent=2)+'\n', encoding='utf-8')
    tmp.replace(ROOT/'release-lock.json')
    print('Requisitos compatibles y SHA-256 fijado. Ejecutá build.ps1 y probá cliente + servidor antes de compartir el EXE.')

if __name__ == '__main__':
    main()
