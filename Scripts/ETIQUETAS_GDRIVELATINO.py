#!/usr/bin/env python3
"""
OK Props (autonomo y rapido)

Sin dependencias de paquetes de Python ni archivos auxiliares (CSV/DLL).
Solo requiere `mkvpropedit` para escribir metadatos MKV.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path


ID_SEGMENT = 0x18538067
ID_TRACKS = 0x1654AE6B
ID_TRACK_ENTRY = 0xAE
ID_TRACK_TYPE = 0x83
ID_TRACK_LANGUAGE = 0x22B59C
ID_TRACK_LANGUAGE_IETF = 0x22B59D
ID_TRACK_FLAG_FORCED = 0x55AA
ID_ATTACHMENTS = 0x1941A469
ID_ATTACHED_FILE = 0x61A7
ID_CLUSTER = 0x1F43B675

TRACK_TYPE_MAP = {1: "video", 2: "audio", 17: "subtitles"}
SPECIAL_RE = re.compile(r"[<>'\"\s\[\],]+")

LANG_NAMES = {
    "video": "Video",
    "und": "Indefinido",
    "es": "Español",
    "es-419": "Español Latino",
    "es-es": "Español",
    "es-la": "Español Latino",
    "spa": "Español",
    "en": "Inglés",
    "en-us": "Inglés",
    "en-gb": "Inglés",
    "eng": "Inglés",
    "pt": "Portugués",
    "por": "Portugués",
    "it": "Italiano",
    "ita": "Italiano",
    "fr": "Francés",
    "fra": "Francés",
    "fre": "Francés",
    "ja": "Japonés",
    "jpn": "Japonés",
    "de": "Alemán",
    "deu": "Alemán",
    "ger": "Alemán",
    "ru": "Ruso",
    "rus": "Ruso",
    "zh": "Chino",
    "zho": "Chino",
    "chi": "Chino",
    "ko": "Coreano",
    "kor": "Coreano",
}

BENIGN_OUTPUT_LINES = {
    "El archivo está siendo analizado.",
    "The file is being analyzed.",
    "Los cambios son escritos al archivo.",
    "The changes are written to the file.",
    "Realizado.",
    "Done.",
}
ATTACHMENT_WARN_RE = re.compile(
    r"(Ningún archivo adjunto coincide con la especificación|Ningun archivo adjunto coincide con la especificación|No attachment matched the specification)",
    re.IGNORECASE,
)
ANSI = {
    "reset": "\033[0m",
    "red": "\033[31m",
    "green": "\033[32m",
    "yellow": "\033[33m",
    "cyan": "\033[36m",
}


@dataclass(slots=True)
class TrackInfo:
    order_index: int
    kind: str
    language: str
    forced: bool


@dataclass(slots=True)
class ProcessResult:
    file_name: str
    ok: bool
    warning: bool
    message: str


def supports_color() -> bool:
    if os.getenv("NO_COLOR"):
        return False
    if not sys.stdout.isatty():
        return False
    if os.name == "nt":
        # Intenta habilitar ANSI en consolas de Windows modernas.
        os.system("")
    return True


USE_COLOR = supports_color()


def paint(text: str, color: str) -> str:
    if not USE_COLOR:
        return text
    code = ANSI.get(color)
    if not code:
        return text
    return f"{code}{text}{ANSI['reset']}"


def short_text(value: str, max_len: int = 160) -> str:
    clean = " ".join(value.split())
    if len(clean) <= max_len:
        return clean
    return clean[: max_len - 3] + "..."


def sanitize_name(name: str) -> str:
    cleaned = SPECIAL_RE.sub("_", name)
    cleaned = re.sub(r"_+", "_", cleaned).strip("._")
    return cleaned or "archivo"


def unique_path(path: Path) -> Path:
    if not path.exists():
        return path
    stem = path.stem
    suffix = path.suffix
    idx = 1
    while True:
        candidate = path.with_name(f"{stem}__{idx}{suffix}")
        if not candidate.exists():
            return candidate
        idx += 1


def safe_rename(src: Path, dst: Path, dry_run: bool) -> Path:
    if src == dst:
        return src
    dst_final = unique_path(dst)
    if dry_run:
        print(f"[DRY] Renombrar: {src.name} -> {dst_final.name}")
        return src
    src.rename(dst_final)
    return dst_final


def safe_move(src: Path, dst_dir: Path, dry_run: bool) -> None:
    dst_final = unique_path(dst_dir / src.name)
    if dry_run:
        print(f"[DRY] Mover: {src.name} -> {dst_final}")
        return
    src.replace(dst_final)


def ensure_dirs(root: Path) -> dict[str, Path]:
    paths = {
        "videos": root / "Videos",
        "subs": root / "Subs",
        "audios": root / "Audios",
        "recursos": root / "Recursos",
        "completado": root / "Completado",
        "originales": root / "Originales",
    }
    for p in paths.values():
        p.mkdir(parents=True, exist_ok=True)
    return paths


def find_mkvpropedit(root: Path, user_path: str | None) -> Path | None:
    if user_path:
        p = Path(user_path)
        return p if p.exists() else None
    candidates = [
        root / "Recursos" / "mkvpropedit.exe",
        root / "mkvpropedit.exe",
    ]
    for c in candidates:
        if c.exists():
            return c
    path_cmd = shutil.which("mkvpropedit")
    return Path(path_cmd) if path_cmd else None


def read_vint_id(buf: memoryview, pos: int, end: int) -> tuple[int, int]:
    if pos >= end:
        raise ValueError("EOF leyendo ID")
    first = buf[pos]
    mask = 0x80
    length = 1
    while length <= 4 and (first & mask) == 0:
        mask >>= 1
        length += 1
    if length > 4 or pos + length > end:
        raise ValueError("ID EBML invalido")
    value = 0
    for i in range(length):
        value = (value << 8) | buf[pos + i]
    return value, length


def read_vint_size(buf: memoryview, pos: int, end: int) -> tuple[int | None, int]:
    if pos >= end:
        raise ValueError("EOF leyendo tamano")
    first = buf[pos]
    mask = 0x80
    length = 1
    while length <= 8 and (first & mask) == 0:
        mask >>= 1
        length += 1
    if length > 8 or pos + length > end:
        raise ValueError("Size EBML invalido")
    value = first & (mask - 1)
    for i in range(1, length):
        value = (value << 8) | buf[pos + i]
    unknown = value == (1 << (7 * length)) - 1
    return (None if unknown else value), length


def read_uint(buf: memoryview, start: int, end: int) -> int:
    value = 0
    for i in range(start, end):
        value = (value << 8) | buf[i]
    return value


def read_text(buf: memoryview, start: int, end: int) -> str:
    raw = bytes(buf[start:end])
    return raw.decode("utf-8", errors="ignore").strip("\x00 ").strip()


def parse_track_entry(buf: memoryview, start: int, end: int, edit_index: int) -> TrackInfo:
    pos = start
    t_type = "unknown"
    lang = "und"
    lang_ietf = ""
    forced = False

    while pos < end:
        elem_id, id_len = read_vint_id(buf, pos, end)
        pos += id_len
        size, size_len = read_vint_size(buf, pos, end)
        pos += size_len
        if size is None:
            elem_end = end
        else:
            elem_end = min(end, pos + size)

        if elem_id == ID_TRACK_TYPE:
            t_type = TRACK_TYPE_MAP.get(read_uint(buf, pos, elem_end), "unknown")
        elif elem_id == ID_TRACK_LANGUAGE:
            value = read_text(buf, pos, elem_end)
            if value:
                lang = value
        elif elem_id == ID_TRACK_LANGUAGE_IETF:
            value = read_text(buf, pos, elem_end)
            if value:
                lang_ietf = value
        elif elem_id == ID_TRACK_FLAG_FORCED:
            forced = read_uint(buf, pos, elem_end) != 0

        pos = elem_end

    chosen_lang = (lang_ietf or lang or "und").strip()
    return TrackInfo(order_index=edit_index, kind=t_type, language=chosen_lang, forced=forced)


def parse_tracks(buf: memoryview, start: int, end: int) -> list[TrackInfo]:
    tracks: list[TrackInfo] = []
    pos = start
    while pos < end:
        elem_id, id_len = read_vint_id(buf, pos, end)
        pos += id_len
        size, size_len = read_vint_size(buf, pos, end)
        pos += size_len
        elem_end = end if size is None else min(end, pos + size)

        if elem_id == ID_TRACK_ENTRY:
            track = parse_track_entry(buf, pos, elem_end, len(tracks) + 1)
            tracks.append(track)
        pos = elem_end
    return tracks


def parse_attachments_count(buf: memoryview, start: int, end: int) -> int:
    count = 0
    pos = start
    while pos < end:
        elem_id, id_len = read_vint_id(buf, pos, end)
        pos += id_len
        size, size_len = read_vint_size(buf, pos, end)
        pos += size_len
        elem_end = end if size is None else min(end, pos + size)
        if elem_id == ID_ATTACHED_FILE:
            count += 1
        pos = elem_end
    return count


def parse_mkv_header(path: Path, max_scan_bytes: int = 64 * 1024 * 1024) -> tuple[list[TrackInfo], int]:
    file_size = path.stat().st_size
    to_read = min(file_size, max_scan_bytes)
    with path.open("rb") as f:
        raw = memoryview(f.read(to_read))

    tracks: list[TrackInfo] = []
    attachments = 0

    pos = 0
    end = len(raw)
    while pos < end:
        elem_id, id_len = read_vint_id(raw, pos, end)
        pos += id_len
        size, size_len = read_vint_size(raw, pos, end)
        pos += size_len
        elem_end = end if size is None else min(end, pos + size)

        if elem_id == ID_SEGMENT:
            seg_pos = pos
            seg_end = elem_end
            while seg_pos < seg_end:
                child_id, child_id_len = read_vint_id(raw, seg_pos, seg_end)
                seg_pos += child_id_len
                child_size, child_size_len = read_vint_size(raw, seg_pos, seg_end)
                seg_pos += child_size_len
                child_end = seg_end if child_size is None else min(seg_end, seg_pos + child_size)

                if child_id == ID_TRACKS:
                    tracks = parse_tracks(raw, seg_pos, child_end)
                elif child_id == ID_ATTACHMENTS:
                    attachments = parse_attachments_count(raw, seg_pos, child_end)
                elif child_id == ID_CLUSTER and tracks:
                    break

                seg_pos = child_end
            break
        pos = elem_end

    if not tracks:
        raise RuntimeError(f"No se pudieron leer tracks MKV en {path.name}.")
    return tracks, attachments


def normalize_lang(code: str) -> str:
    c = code.strip().lower().replace("_", "-")
    return c or "und"


def pretty_lang(code: str) -> str:
    c = normalize_lang(code)
    if c in LANG_NAMES:
        return LANG_NAMES[c]
    base = c.split("-")[0]
    if base in LANG_NAMES:
        return LANG_NAMES[base]
    return code


def build_track_name(track: TrackInfo, brand: str) -> str:
    base = pretty_lang("video" if track.kind == "video" else track.language)
    suffix = " [Forzados]" if track.forced else ""
    return f"{base}{suffix} [{brand}]"


def run_cmd(cmd: list[str], dry_run: bool) -> tuple[int, str]:
    if dry_run:
        shown = " ".join(f'"{c}"' if " " in c else c for c in cmd)
        return 0, f"[DRY] {shown}"
    proc = subprocess.run(
        cmd,
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    return proc.returncode, proc.stdout or ""


def classify_mkvpropedit_output(exit_code: int, output: str) -> tuple[bool, int, str | None]:
    lines = [line.strip() for line in output.splitlines() if line.strip()]
    warning_count = 0
    unexpected: list[str] = []

    for line in lines:
        if line in BENIGN_OUTPUT_LINES:
            continue
        if ATTACHMENT_WARN_RE.search(line):
            warning_count += 1
            continue
        lower = line.lower()
        if lower.startswith("advertencia:") and ATTACHMENT_WARN_RE.search(line):
            warning_count += 1
            continue
        if lower.startswith("warning:") and ATTACHMENT_WARN_RE.search(line):
            warning_count += 1
            continue
        unexpected.append(line)

    if unexpected:
        explicit_error = next((line for line in unexpected if "error" in line.lower()), unexpected[0])
        return False, warning_count, short_text(explicit_error)

    if exit_code == 0:
        return True, warning_count, None

    if exit_code == 1 and warning_count > 0:
        return True, warning_count, None

    if not lines:
        return False, warning_count, f"mkvpropedit fallo (exit={exit_code}) sin salida"
    return False, warning_count, f"mkvpropedit fallo (exit={exit_code})"


def sanitize_completado(completado_dir: Path, dry_run: bool) -> list[Path]:
    out: list[Path] = []
    for p in completado_dir.iterdir():
        if not p.is_file():
            continue
        cleaned = sanitize_name(p.name)
        if cleaned != p.name:
            p = safe_rename(p, completado_dir / cleaned, dry_run)
        out.append(p)
    return out


def process_mkv(
    mkv_file: Path,
    mkvpropedit: Path,
    brand: str,
    uploader: str,
    cover_path: Path | None,
    dry_run: bool,
) -> ProcessResult:
    try:
        tracks, attachments_count = parse_mkv_header(mkv_file)
    except Exception as exc:
        return ProcessResult(
            file_name=mkv_file.name,
            ok=False,
            warning=False,
            message=f"error leyendo MKV ({short_text(str(exc))})",
        )

    cmd = [
        str(mkvpropedit),
        str(mkv_file),
        "--edit",
        "info",
        "--set",
        f"title=[{brand}]",
        "--set",
        f"muxing-application={uploader} x {brand}.net",
        "--set",
        "writing-application=BZK90 Script",
        "--tags",
        "all:",
    ]

    for _ in range(max(0, attachments_count)):
        cmd += ["--delete-attachment", "1"]

    video_idx = 1
    audio_idx = 1
    sub_idx = 1
    for track in tracks:
        selector = f"track:{track.order_index}"
        if track.kind == "video":
            selector = f"track:v{video_idx}"
            video_idx += 1
        elif track.kind == "audio":
            selector = f"track:a{audio_idx}"
            audio_idx += 1
        elif track.kind == "subtitles":
            selector = f"track:s{sub_idx}"
            sub_idx += 1
        new_name = build_track_name(track, brand)
        cmd += ["--edit", selector, "--set", f"name={new_name}"]

    if cover_path and cover_path.exists():
        cmd += [
            "--attachment-name",
            f"www.{brand}.net",
            "--attachment-mime-type",
            "image/jpeg",
            "--add-attachment",
            str(cover_path),
        ]

    cmd += ["--ui-language", "es"]

    code, output = run_cmd(cmd, dry_run=dry_run)
    ok, warning_count, reason = classify_mkvpropedit_output(code, output)
    if not ok:
        return ProcessResult(
            file_name=mkv_file.name,
            ok=False,
            warning=False,
            message=reason or f"mkvpropedit fallo (exit={code})",
        )

    msg = f"actualizado (pistas={len(tracks)})"
    if warning_count:
        msg += f", advertencias={warning_count}"
    return ProcessResult(
        file_name=mkv_file.name,
        ok=True,
        warning=warning_count > 0,
        message=msg,
    )


def move_group(source_dir: Path, originales_dir: Path, dry_run: bool) -> int:
    moved = 0
    for p in source_dir.iterdir():
        if not p.is_file():
            continue
        safe_move(p, originales_dir, dry_run)
        moved += 1
    return moved


def launch_renombrar(root: Path, dry_run: bool) -> None:
    shortcut = root / "Renombrar.lnk"
    if not shortcut.exists():
        print(paint(f'No se encontro "{shortcut}".', "yellow"))
        return
    if dry_run:
        print(f"[DRY] Lanzar: {shortcut}")
        return
    try:
        os.startfile(str(shortcut))
        print(paint("Renombrar.lnk lanzado.", "cyan"))
    except OSError as exc:
        print(paint(f"No se pudo abrir Renombrar.lnk ({short_text(str(exc))})", "yellow"))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="OK Props autonomo y rapido")
    parser.add_argument("--brand", default="GDriveLatinoHD", help="Marca final.")
    parser.add_argument("--uploader", default="el_inmortus", help="Texto de muxing-application.")
    parser.add_argument("--mkvpropedit", help="Ruta manual de mkvpropedit(.exe).")
    parser.add_argument("--cover", help="Ruta de imagen JPG para adjunto (opcional).")
    parser.add_argument("--workers", type=int, default=max(1, min(4, os.cpu_count() or 1)))
    parser.add_argument("--dry-run", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    root = Path.cwd()
    dirs = ensure_dirs(root)

    mkvpropedit = find_mkvpropedit(root, args.mkvpropedit)
    if not mkvpropedit:
        print("No se encontro mkvpropedit.exe.")
        print("Colocalo en `Recursos\\mkvpropedit.exe` o pasa --mkvpropedit <ruta>.")
        return 1

    cover_path = Path(args.cover) if args.cover else (dirs["recursos"] / "GDLHD.jpg")
    if not cover_path.exists():
        cover_path = None

    files = sanitize_completado(dirs["completado"], dry_run=args.dry_run)
    mkv_files = [p for p in files if p.suffix.lower() == ".mkv" and p.exists()]
    if not mkv_files:
        print("-------------------------------------")
        print("--- NO HAY ARCHIVOS PARA TRABAJAR ---")
        print("-------------------------------------")
        return 0

    ok_count = 0
    warn_count = 0
    fail_count = 0
    errors: list[str] = []
    workers = max(1, args.workers)
    total = len(mkv_files)
    print(paint(f"Procesando {total} MKV con {workers} worker(s)...", "cyan"))

    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as ex:
        future_map = {
            ex.submit(
                process_mkv,
                mkv_file,
                mkvpropedit,
                args.brand,
                args.uploader,
                cover_path,
                args.dry_run,
            ): mkv_file
            for mkv_file in mkv_files
        }
        idx = 0
        for future in concurrent.futures.as_completed(future_map):
            idx += 1
            mkv_file = future_map[future]
            try:
                result = future.result()
            except Exception as exc:
                result = ProcessResult(
                    file_name=mkv_file.name,
                    ok=False,
                    warning=False,
                    message=f"error inesperado ({short_text(str(exc))})",
                )

            if result.ok and result.warning:
                warn_count += 1
                status = paint("WARN", "yellow")
            elif result.ok:
                ok_count += 1
                status = paint("OK", "green")
            else:
                fail_count += 1
                status = paint("ERROR", "red")
                errors.append(f"{result.file_name}: {result.message}")

            print(f"[{idx:>2}/{total}] {status} {result.file_name} | {result.message}")

    moved_v = move_group(dirs["videos"], dirs["originales"], args.dry_run)
    moved_s = move_group(dirs["subs"], dirs["originales"], args.dry_run)
    moved_a = move_group(dirs["audios"], dirs["originales"], args.dry_run)
    if moved_v > 0:
        print(f'Moviendo {moved_v} archivo(s) de video a "Originales"...')
    if moved_s > 0:
        print(f'Moviendo {moved_s} archivo(s) de subtitulos a "Originales"...')
    if moved_a > 0:
        print(f'Moviendo {moved_a} archivo(s) de audio a "Originales"...')

    print("----------------------------------")
    print("----------- FINALIZADO -----------")
    print("----------------------------------")
    print(f"MKV OK: {paint(str(ok_count), 'green')}")
    print(f"MKV WARN: {paint(str(warn_count), 'yellow')}")
    print(f"MKV ERROR: {paint(str(fail_count), 'red')}")
    print(f"Movidos: Videos={moved_v}, Subs={moved_s}, Audios={moved_a}")
    if errors:
        print(paint("Errores detectados:", "red"))
        for item in errors:
            print(f"- {item}")

    launch_renombrar(root, args.dry_run)

    return 0 if fail_count == 0 else 2


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("\nCancelado por usuario.")
        raise SystemExit(130)
