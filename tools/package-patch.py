import os
import zipfile
import shutil

src_dir = r"E:\Genesis Launcher - DEV"
dest_files = [
    r"E:\konsor-ro3\site\download\konsor-ro3-patch.zip",
    r"E:\KonsorRO_Release\konsor-ro3-patch.zip",
    os.path.expanduser(r"~\Downloads\konsor-ro3-patch.zip")
]

for d in dest_files:
    os.makedirs(os.path.dirname(d), exist_ok=True)

files_to_pack = [
    ('KonsorRO.exe', 'konsor-ro3/KonsorRO.exe'),
    ('RO3Emu.exe', 'konsor-ro3/RO3Emu.exe'),
    ('cloudflared.exe', 'konsor-ro3/cloudflared.exe'),
    ('WebView2Loader.dll', 'konsor-ro3/WebView2Loader.dll'),
    ('e_sqlite3.dll', 'konsor-ro3/e_sqlite3.dll'),
    ('D3DCompiler_47_cor3.dll', 'konsor-ro3/D3DCompiler_47_cor3.dll'),
    ('PenImc_cor3.dll', 'konsor-ro3/PenImc_cor3.dll'),
    ('PresentationNative_cor3.dll', 'konsor-ro3/PresentationNative_cor3.dll'),
    ('vcruntime140_cor3.dll', 'konsor-ro3/vcruntime140_cor3.dll'),
    ('wpfgfx_cor3.dll', 'konsor-ro3/wpfgfx_cor3.dll'),
    ('launcher.json', 'konsor-ro3/launcher.json'),
    ('news.json', 'konsor-ro3/news.json'),
    ('LEEME.txt', 'konsor-ro3/LEEME.txt'),
    ('run_konsor_ro3.bat', 'konsor-ro3/run_konsor_ro3.bat'),
    ('konsor.ps1', 'konsor-ro3/konsor.ps1'),
    ('restore_official.bat', 'konsor-ro3/restore_official.bat'),
    (os.path.join('client', 'Ro3Emu.ClientHooks.dll'), 'konsor-ro3/client/Ro3Emu.ClientHooks.dll'),
    (os.path.join('client', 'Ro3Emu.Loc.dll'), 'konsor-ro3/client/Ro3Emu.Loc.dll'),
]

hero_art = os.path.join(src_dir, 'assets', 'hero-art.jpg')
if os.path.exists(hero_art):
    files_to_pack.append((os.path.join('assets', 'hero-art.jpg'), 'konsor-ro3/assets/hero-art.jpg'))

primary_zip = dest_files[0]
print(f"Generando {primary_zip}...")
with zipfile.ZipFile(primary_zip, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for src_rel, arcname in files_to_pack:
        full_src = os.path.join(src_dir, src_rel)
        if os.path.exists(full_src):
            z.write(full_src, arcname)
        else:
            print(f"[AVISO] Archivo no encontrado: {full_src}")

size_mb = os.path.getsize(primary_zip) / (1024 * 1024)
print(f"[OK] Archivo principal generado ({size_mb:.2f} MB).")

for copy_dest in dest_files[1:]:
    shutil.copy2(primary_zip, copy_dest)
    print(f"[OK] Copiado a: {copy_dest}")
