#!/usr/bin/env python3
"""
OK Props (autonomo y rapido)

Sin dependencias de paquetes de Python ni archivos auxiliares (CSV/DLL).
Solo requiere `mkvpropedit` para escribir metadatos MKV.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path


ID_SEGMENT = 0x18538067
ID_TRACKS = 0x1654AE6B
ID_TRACK_ENTRY = 0xAE
ID_TRACK_TYPE = 0x83
ID_TRACK_NAME = 0x536E
ID_TRACK_LANGUAGE = 0x22B59C
ID_TRACK_LANGUAGE_IETF = 0x22B59D
ID_TRACK_FLAG_FORCED = 0x55AA
ID_ATTACHMENTS = 0x1941A469
ID_ATTACHED_FILE = 0x61A7
ID_CLUSTER = 0x1F43B675

TRACK_TYPE_MAP = {1: "video", 2: "audio", 17: "subtitles"}
SPECIAL_RE = re.compile(r"[<>'\"\s\[\],]+")
YEAR_RE = re.compile(r"\b(?P<year>19\d{2}|20\d{2})\b")
RESOLUTION_RE = re.compile(r"\b(?P<resolution>4320p|2160p|1080p|720p|480p)\b", re.IGNORECASE)
SOURCE_QUALITY_RE = re.compile(r"\b(?P<source>BDREMUX|REMUX|WEB[-_. ]?DL|WEBRIP|BLURAY|BDRIP|HDRip|DVDRip|HDTV|HDTS|TS|TC|CAM)\b", re.IGNORECASE)
SEASON_EPISODE_RE = re.compile(r"\bS\d{1,2}E\d{1,3}\b", re.IGNORECASE)
EPISODE_DASH_RE = re.compile(r"\s+-\s+(?:S\d{1,2}E\d{1,3}|\d{1,3})\b.*$", re.IGNORECASE)
IMDB_SUGGEST_URL = "https://v3.sg.media-imdb.com/suggestion/{prefix}/{query}.json"
POSTER_FILE_NAME = "cover.jpg"
SMALL_POSTER_FILE_NAME = "small_cover.jpg"
DEFAULT_FILEBOT_EXE = Path(r"C:\Users\gilbe\OneDrive\Documentos\ENCODER_INMORTUS\2 Rename\filebot.exe")

FALLBACK_LANG_NAMES = {
    "video": "Video",
    "und": "Indefinido",
    "es": "Español",
    "es-419": "Español Latino",
    "es-es": "Español (España)",
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
    "fa": "Persa",
    "fas": "Persa",
    "per": "Persa",
    "th": "Tailandés",
    "tha": "Tailandés",
}

FALLBACK_CANONICAL_CODES = {
    "video": "video",
    "und": "und",
    "es": "es",
    "spa": "es",
    "es-419": "es-419",
    "es-la": "es-419",
    "es-es": "es-es",
    "en": "en",
    "eng": "en",
    "en-us": "en",
    "en-gb": "en",
    "pt": "pt",
    "por": "pt",
    "it": "it",
    "ita": "it",
    "fr": "fr",
    "fra": "fr",
    "fre": "fr",
    "ja": "ja",
    "jpn": "ja",
    "de": "de",
    "deu": "de",
    "ger": "de",
    "ru": "ru",
    "rus": "ru",
    "zh": "zh",
    "zho": "zh",
    "chi": "zh",
    "ko": "ko",
    "kor": "ko",
    "fa": "fa",
    "fas": "fa",
    "per": "fa",
    "th": "th",
    "tha": "th",
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
    r"(Ningún archivo adjunto coincide con la especificación|Ningun archivo adjunto coincide con la especificación|No attachment matched the specification|No attachment matched the spec)",
    re.IGNORECASE,
)
SUBTITLE_PROMO_RE = re.compile(
    r"(?:"
    r"https?://|"
    r"www\.|"
    r"\bt\.me/|"
    r"\btelegram(?:\.me|:)?|"
    r"\b(?:vip\.)?hdlatino\b|"
    r"\blatinomegahd\b|"
    r"\bsscany\b|"
    r"\bel[_\s-]?inmortus\b|"
    r"\bvis[ií]tanos\b|"
    r"\bmembres[ií]a\b|"
    r"\bdescargas?\s+directas?\b|"
    r"\b[a-z0-9][a-z0-9-]{1,63}\.(?:com|net|org|us|me|io|tv)\b"
    r")",
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
    name: str = ""
    language_ietf: str = ""
    metadata_overridden: bool = False


@dataclass(slots=True)
class ProcessResult:
    file_name: str
    ok: bool
    warning: bool
    message: str


@dataclass(slots=True)
class ReleaseIdentity:
    title: str
    year: int | None
    quality: str


@dataclass(slots=True)
class PosterCandidate:
    title: str
    year: int | None
    imdb_id: str
    image_url: str
    score: int


@dataclass(slots=True)
class SubtitleCleanResult:
    changed: bool
    removed: int
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


def clean_release_text(value: str) -> str:
    text = re.sub(r"[._]+", " ", value)
    text = re.sub(r"\s+", " ", text).strip(" -_")
    return text


def extract_release_identity(file_name: str) -> ReleaseIdentity:
    stem = Path(file_name).stem
    normalized = clean_release_text(stem)
    resolution_match = RESOLUTION_RE.search(normalized)
    source_match = SOURCE_QUALITY_RE.search(normalized)
    quality_match = source_match or resolution_match
    year_match = YEAR_RE.search(normalized)
    year = int(year_match.group("year")) if year_match else None
    quality_parts: list[str] = []
    if resolution_match:
        quality_parts.append(resolution_match.group("resolution"))
    if source_match:
        quality_parts.append(source_match.group("source").replace("_", "-").replace(" ", "-"))
    quality = " ".join(dict.fromkeys(quality_parts))

    title_part = normalized
    if year_match:
        title_part = normalized[: year_match.start()]
    elif quality_match:
        title_part = normalized[: quality_match.start()]

    title_part = EPISODE_DASH_RE.sub("", title_part)
    title_part = SEASON_EPISODE_RE.sub("", title_part)
    title_part = re.sub(
        r"\b(?:MULTI|LATINO|DUAL|SUBS?|SUBTITULOS?|AAC|AC3|EAC3|DTS|TRUEHD|ATMOS|DDP?|H(?:\.?26[45])|X(?:\.?26[45])|HEVC|AVC|HDR|DV|NF|AMZN|DSNP|CR|HMAX)\b",
        " ",
        title_part,
        flags=re.IGNORECASE,
    )
    title = clean_release_text(title_part)
    return ReleaseIdentity(title=title, year=year, quality=quality)


def imdb_suggestion_url(query: str) -> str:
    query = clean_release_text(query).lower()
    prefix_match = re.search(r"[a-z0-9]", query)
    prefix = prefix_match.group(0) if prefix_match else "x"
    return IMDB_SUGGEST_URL.format(
        prefix=urllib.parse.quote(prefix, safe=""),
        query=urllib.parse.quote(query, safe=""),
    )


def normalize_imdb_image_url(url: str) -> str:
    return re.sub(r"@\._V1_.*\.(?:jpg|jpeg)$", "@._V1_.jpg", url, flags=re.IGNORECASE)


def title_token_score(left: str, right: str) -> int:
    left_tokens = {t for t in re.findall(r"[a-z0-9]+", left.lower()) if len(t) > 1}
    right_tokens = {t for t in re.findall(r"[a-z0-9]+", right.lower()) if len(t) > 1}
    if not left_tokens or not right_tokens:
        return 0
    return len(left_tokens & right_tokens) * 10


def score_imdb_candidate(item: dict, identity: ReleaseIdentity) -> PosterCandidate | None:
    image = item.get("i")
    if not isinstance(image, dict):
        return None

    image_url = image.get("imageUrl")
    title = item.get("l")
    imdb_id = item.get("id")
    if not isinstance(image_url, str) or not isinstance(title, str) or not isinstance(imdb_id, str):
        return None

    item_year = item.get("y")
    year = item_year if isinstance(item_year, int) else None
    score = title_token_score(identity.title, title)

    if identity.year and year:
        year_delta = abs(identity.year - year)
        if year_delta == 0:
            score += 100
        elif year_delta <= 1:
            score += 30
        else:
            score -= min(60, year_delta * 8)

    qid = str(item.get("qid") or "").lower()
    kind = str(item.get("q") or "").lower()
    if qid in {"movie", "tvmovie", "tvseries", "tvminiseries"} or kind in {"feature", "tv series", "tv mini series"}:
        score += 12
    elif "video game" in kind:
        score -= 30

    return PosterCandidate(
        title=title,
        year=year,
        imdb_id=imdb_id,
        image_url=normalize_imdb_image_url(image_url),
        score=score,
    )


def find_imdb_poster(identity: ReleaseIdentity, timeout: int = 12) -> PosterCandidate | None:
    if not identity.title:
        return None

    query = f"{identity.title} {identity.year}" if identity.year else identity.title
    request = urllib.request.Request(
        imdb_suggestion_url(query),
        headers={
            "Accept": "application/json",
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MediaWorkflowOrchestrator/1.0",
        },
    )
    with urllib.request.urlopen(request, timeout=timeout) as response:
        raw = response.read()

    data = json.loads(raw.decode("utf-8", errors="replace"))
    items = data.get("d", [])
    if not isinstance(items, list):
        return None

    candidates = [
        candidate
        for item in items
        if isinstance(item, dict)
        for candidate in [score_imdb_candidate(item, identity)]
        if candidate is not None
    ]
    if not candidates:
        return None

    candidates.sort(key=lambda candidate: candidate.score, reverse=True)
    best = candidates[0]
    return best if best.score > 0 else None


def download_poster(candidate: PosterCandidate, output_path: Path, dry_run: bool, timeout: int = 20) -> Path | None:
    if dry_run:
        print(f"[DRY] Descargar poster IMDb {candidate.imdb_id}: {candidate.image_url} -> {output_path}")
        return output_path

    request = urllib.request.Request(
        candidate.image_url,
        headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MediaWorkflowOrchestrator/1.0"},
    )
    with urllib.request.urlopen(request, timeout=timeout) as response:
        content_type = response.headers.get("Content-Type", "")
        data = response.read()

    if not data or "image" not in content_type.lower():
        return None

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(data)
    return output_path


def resolve_cover_path(
    recursos_dir: Path,
    mkv_files: list[Path],
    explicit_cover: str | None,
    dry_run: bool,
) -> Path | None:
    if explicit_cover:
        cover_path = Path(explicit_cover)
        if cover_path.exists():
            print(f"Usando portada manual: {cover_path}")
            return cover_path
        print(paint(f"No se encontro la portada manual: {cover_path}", "yellow"))

    source_file = mkv_files[0] if mkv_files else None
    if source_file is not None:
        identity = extract_release_identity(source_file.name)
        parsed = identity.title or source_file.stem
        details = [f'titulo="{parsed}"']
        if identity.year:
            details.append(f"year={identity.year}")
        if identity.quality:
            details.append(f"calidad={identity.quality}")
        print("Buscando poster IMDb: " + ", ".join(details))

        try:
            candidate = find_imdb_poster(identity)
            if candidate:
                output_path = recursos_dir / POSTER_FILE_NAME
                downloaded = download_poster(candidate, output_path, dry_run)
                if downloaded:
                    label_year = f" ({candidate.year})" if candidate.year else ""
                    print(paint(f"Poster IMDb guardado en {downloaded}: {candidate.title}{label_year} [{candidate.imdb_id}]", "cyan"))
                    return downloaded
        except (OSError, urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
            print(paint(f"No se pudo descargar poster IMDb ({short_text(str(exc))}).", "yellow"))

    fallback_path = recursos_dir / "GDLHD.jpg"
    if fallback_path.exists():
        print(f"Usando portada fallback: {fallback_path}")
        return fallback_path

    return None


def normalize_lang(code: str) -> str:
    c = code.strip().lower().replace("_", "-")
    return c or "und"


def normalize_language_hint(value: str) -> str:
    text = re.sub(r"\[[^\]]*\]", " ", value or "")
    text = text.replace("GDriveLatinoHD", " ")
    text = unicodedata.normalize("NFKD", text)
    text = "".join(ch for ch in text if not unicodedata.combining(ch))
    text = text.lower().replace("_", "-")
    text = text.replace("gdrivelatinohd", " ")
    text = re.sub(r"[^a-z0-9-]+", " ", text)
    return re.sub(r"\s+", " ", text).strip()


def has_any_language_hint(values: tuple[str, ...], hints: tuple[str, ...]) -> bool:
    normalized_values = [normalize_language_hint(value) for value in values if value]
    return any(hint in value for value in normalized_values for hint in hints)


def load_language_catalog() -> tuple[dict[str, str], dict[str, str]]:
    catalog_path = Path(__file__).with_name("track_languages.json")
    language_names = dict(FALLBACK_LANG_NAMES)
    canonical_codes = dict(FALLBACK_CANONICAL_CODES)

    if not catalog_path.exists():
        return language_names, canonical_codes

    try:
        raw = json.loads(catalog_path.read_text(encoding="utf-8"))
        display_names = raw.get("displayNames", {})
        if isinstance(display_names, dict):
            for code, label in display_names.items():
                if not isinstance(code, str) or not isinstance(label, str):
                    continue
                normalized = normalize_lang(code)
                clean_label = label.strip()
                if normalized and clean_label:
                    language_names[normalized] = clean_label

        raw_canonical_codes = raw.get("canonicalBaseCodes", {})
        if isinstance(raw_canonical_codes, dict):
            for code, canonical in raw_canonical_codes.items():
                if not isinstance(code, str) or not isinstance(canonical, str):
                    continue
                normalized = normalize_lang(code)
                normalized_canonical = normalize_lang(canonical)
                if normalized and normalized_canonical:
                    canonical_codes[normalized] = normalized_canonical
    except Exception:
        return language_names, canonical_codes

    return language_names, canonical_codes


LANG_NAMES, LANG_CANONICAL_CODES = load_language_catalog()
CASTELLANO_HINTS = (
    "castellano",
    "es-es",
    "espanol espana",
    "espanol de espana",
    "spanish spain",
    "spanish from spain",
)
LATINO_HINTS = (
    "es-419",
    "es-la",
    "es-lat",
    "latino",
    "latam",
    "latinoamerica",
    "latinoamericano",
    "latin america",
    "latin-america",
    "latinamerican",
)
LEGACY_LANGUAGE_CODES = {
    "ar": "ara",
    "de": "ger",
    "en": "eng",
    "es": "spa",
    "fa": "per",
    "fr": "fre",
    "it": "ita",
    "ja": "jpn",
    "ko": "kor",
    "pt": "por",
    "th": "tha",
    "und": "und",
    "zh": "chi",
}
AUDIO_DETAIL_SPLIT_RE = re.compile(r"\s*/\s*|\s+\|\s+")
AUDIO_CHANNEL_RE = re.compile(r"\b(?:1\.0|2\.0|5\.1(?:-EX)?|6\.1|7\.1|9\.1|11\.1|13\.1)(?:\+\d+\s*objects?)?\b", re.IGNORECASE)
AUDIO_MEANINGFUL_LABEL_RE = re.compile(
    r"\b(?:"
    r"mix|mezcla|track|pista|version|versi[oó]n|dub|doblaje|dublado|synchro|"
    r"commentary|comentario|comentarios|director|critic|cr[ií]tico|historian|historiador|"
    r"theatrical|cine|cinema|compatibility|compatible|extended|director'?s\s+cut"
    r")\b",
    re.IGNORECASE,
)
AUDIO_TECH_ONLY_RE = re.compile(
    r"\b(?:dolby|truehd|dts(?:-hd)?|master\s+audio|atmos\s+audio|digital(?:\s+plus)?|"
    r"ac-?3|e-?ac-?3|aac|flac|pcm|lpcm|mlp|opus|vorbis|mp3|mpeg|audio|"
    r"mono|stereo|est[eé]reo|surround|lossless|lossy|hi-?res|"
    r"khz|hz|kbps|mbps|bit|bits|objects?|canales|channels?)\b",
    re.IGNORECASE,
)
AUDIO_REDUNDANT_DETAIL_HINTS = {
    "latino",
    "latam",
    "latin america",
    "latin-america",
    "latinoamerica",
    "latinoamericano",
    "castellano",
    "espanol",
    "spanish",
}


def resolve_lang_code(code: str) -> str | None:
    normalized = normalize_lang(code)
    if normalized in LANG_NAMES:
        return normalized

    canonical = LANG_CANONICAL_CODES.get(normalized)
    if canonical and canonical in LANG_NAMES:
        return canonical

    base = normalized.split("-")[0]
    if base in LANG_NAMES:
        return base

    canonical_base = LANG_CANONICAL_CODES.get(base)
    if canonical_base and canonical_base in LANG_NAMES:
        return canonical_base

    return None


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


def find_mkvtool(root: Path, tool_name: str, user_path: str | None, sibling: Path | None = None) -> Path | None:
    if user_path:
        p = Path(user_path)
        return p if p.exists() else None

    exe_name = f"{tool_name}.exe" if os.name == "nt" else tool_name
    candidates: list[Path] = []
    if sibling is not None:
        candidates.append(sibling.parent / exe_name)
    candidates += [
        root / "Recursos" / exe_name,
        root / exe_name,
    ]
    for c in candidates:
        if c.exists():
            return c

    path_cmd = shutil.which(tool_name)
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
    track_name = ""
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
        elif elem_id == ID_TRACK_NAME:
            value = read_text(buf, pos, elem_end)
            if value:
                track_name = value
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
    return TrackInfo(
        order_index=edit_index,
        kind=t_type,
        language=chosen_lang,
        forced=forced,
        name=track_name,
        language_ietf=lang_ietf,
    )


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


def pretty_lang(code: str) -> str:
    resolved = resolve_lang_code(code)
    if resolved:
        return LANG_NAMES[resolved]
    return code


def resolve_track_label(track: TrackInfo) -> str:
    if track.kind == "video":
        return pretty_lang("video")

    visible_values = (track.name,)
    metadata_values = (track.language_ietf, track.language)
    combined_values = visible_values + metadata_values

    if has_any_language_hint(visible_values, CASTELLANO_HINTS):
        return "Castellano"

    if has_any_language_hint(combined_values, LATINO_HINTS):
        return "Español Latino"

    if has_any_language_hint(metadata_values, CASTELLANO_HINTS):
        return "Castellano"

    return pretty_lang(track.language)


def clean_audio_detail_segment(value: str, brand: str, base_label: str) -> str:
    text = value.strip()
    if not text:
        return ""

    text = re.sub(rf"\[{re.escape(brand)}\]", " ", text, flags=re.IGNORECASE)
    text = text.replace(brand, " ")
    text = re.sub(r"\s+", " ", text).strip(" -")
    text = strip_leading_audio_labels(text, brand, base_label)
    if not text:
        return ""

    normalized = normalize_language_hint(text)
    language_labels = {normalize_language_hint(label) for label in LANG_NAMES.values()}
    language_labels.update(
        {
            normalize_language_hint(base_label),
            normalize_language_hint(brand),
            "espanol latino",
            "espanol latinoamerica",
            "espanol espana",
            "castellano",
        }
    )
    if normalized in language_labels:
        return ""

    return text


def strip_leading_audio_labels(value: str, brand: str, base_label: str) -> str:
    text = value.strip(" -")
    labels = {label for label in LANG_NAMES.values() if label}
    labels.update(
        {
            base_label,
            brand,
            "Español Latino",
            "Español (Latinoamérica)",
            "Español Latinoamérica",
            "Castellano",
            "Inglés",
            "English",
            "Spanish",
            "Latino",
        }
    )

    changed = True
    while changed and text:
        changed = False
        for label in sorted(labels, key=len, reverse=True):
            if not label:
                continue
            next_text = re.sub(rf"^\s*{re.escape(label)}(?:\s+|$)", "", text, count=1, flags=re.IGNORECASE).strip(" -")
            if next_text != text:
                text = next_text
                changed = True
                break

    return text


def is_redundant_audio_detail(part: str, base_label: str) -> bool:
    normalized = normalize_language_hint(part)
    if not normalized:
        return True

    if normalized in AUDIO_REDUNDANT_DETAIL_HINTS:
        return True

    base_tokens = set(normalize_language_hint(base_label).split())
    detail_tokens = set(normalized.split())
    return bool(detail_tokens) and detail_tokens.issubset(base_tokens)


def normalize_audio_channel_label(value: str) -> str:
    match = AUDIO_CHANNEL_RE.search(value)
    if not match:
        return ""

    channel = match.group(0)
    return re.sub(r"\+\d+\s*objects?", "", channel, flags=re.IGNORECASE)


def build_audio_detail(track: TrackInfo, brand: str, base_label: str) -> str:
    if track.kind != "audio" or not track.name.strip():
        return ""

    pieces: list[str] = []
    seen: set[str] = set()
    for raw_part in AUDIO_DETAIL_SPLIT_RE.split(track.name):
        part = clean_audio_detail_segment(raw_part, brand, base_label)
        if not part:
            continue

        if AUDIO_CHANNEL_RE.fullmatch(part):
            continue
        if is_redundant_audio_detail(part, base_label):
            continue

        meaningful = AUDIO_MEANINGFUL_LABEL_RE.search(part) is not None
        technical = AUDIO_TECH_ONLY_RE.search(part) is not None
        if technical and not meaningful:
            continue

        key = normalize_language_hint(part)
        if key and key not in seen:
            pieces.append(part)
            seen.add(key)

    detail = " ".join(pieces).strip()
    channel = normalize_audio_channel_label(track.name)
    if channel and not re.search(rf"\b{re.escape(channel)}\b", detail, re.IGNORECASE):
        detail = f"{detail} {channel}".strip()

    return detail


def build_track_name(track: TrackInfo, brand: str) -> str:
    base = resolve_track_label(track)
    suffix = " [Forzados]" if track.forced else ""
    detail = build_audio_detail(track, brand, base)
    if detail:
        return f"{base}{suffix} [{brand}] {detail}"
    return f"{base}{suffix} [{brand}]"


def canonical_language_code(code: str) -> str:
    normalized = normalize_lang(code)
    canonical = LANG_CANONICAL_CODES.get(normalized)
    if canonical:
        return canonical

    base = normalized.split("-")[0]
    canonical_base = LANG_CANONICAL_CODES.get(base)
    if canonical_base:
        return canonical_base

    return normalized


def legacy_language_code(code: str) -> str | None:
    normalized = canonical_language_code(code)
    base = normalized.split("-")[0]
    if len(normalized) == 3:
        return normalized
    if base in LEGACY_LANGUAGE_CODES:
        return LEGACY_LANGUAGE_CODES[base]
    if len(base) == 3:
        return base
    return None


def load_track_metadata_overrides(path: str | None) -> dict[str, list[dict[str, object]]]:
    empty: dict[str, list[dict[str, object]]] = {"audio": [], "subtitles": []}
    if not path:
        return empty

    override_path = Path(path)
    if not override_path.exists():
        print(paint(f"Metadata manual de tracks omitida: no existe {override_path}", "yellow"))
        return empty

    try:
        raw = json.loads(override_path.read_text(encoding="utf-8-sig"))
    except Exception as exc:
        print(paint(f"Metadata manual de tracks omitida: {short_text(str(exc))}", "yellow"))
        return empty

    if not isinstance(raw, dict):
        return empty

    loaded: dict[str, list[dict[str, object]]] = {"audio": [], "subtitles": []}
    for key in loaded:
        values = raw.get(key)
        if not isinstance(values, list):
            continue
        loaded[key] = [item for item in values if isinstance(item, dict)]

    total = len(loaded["audio"]) + len(loaded["subtitles"])
    if total:
        print(f"Metadata manual de tracks cargada: {total} override(s).")
    return loaded


def apply_track_metadata_overrides(
    tracks: list[TrackInfo],
    overrides: dict[str, list[dict[str, object]]],
) -> None:
    positions = {"audio": 0, "subtitles": 0}
    for track in tracks:
        if track.kind not in positions:
            continue

        index = positions[track.kind]
        positions[track.kind] += 1
        items = overrides.get(track.kind) or []
        if index >= len(items):
            continue

        item = items[index]
        language_code = item.get("languageCode")
        if isinstance(language_code, str) and language_code.strip():
            canonical = canonical_language_code(language_code)
            track.language = canonical
            track.language_ietf = canonical
            track.metadata_overridden = True

        track_name = item.get("name")
        if isinstance(track_name, str):
            track.name = track_name.strip()


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
        if line.startswith("[DRY]"):
            continue
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


def run_json_cmd(cmd: list[str]) -> dict:
    proc = subprocess.run(
        cmd,
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if proc.returncode != 0:
        detail = short_text(proc.stderr or proc.stdout or f"exit={proc.returncode}")
        raise RuntimeError(detail)
    try:
        return json.loads(proc.stdout)
    except json.JSONDecodeError as exc:
        raise RuntimeError(f"JSON invalido ({short_text(str(exc))})") from exc


def subtitle_track_extension(track: dict) -> str | None:
    props = track.get("properties") or {}
    codec_id = str(props.get("codec_id") or "").upper()
    codec = str(track.get("codec") or "").lower()
    if codec_id == "S_TEXT/UTF8" or "subrip" in codec:
        return ".srt"
    if codec_id == "S_TEXT/ASS" or "ass" in codec:
        return ".ass"
    if codec_id == "S_TEXT/SSA" or "ssa" in codec:
        return ".ssa"
    if codec_id == "S_TEXT/WEBVTT" or "webvtt" in codec:
        return ".vtt"
    return None


def subtitle_has_promo(text: str) -> bool:
    return SUBTITLE_PROMO_RE.search(text) is not None


def renumber_srt_blocks(blocks: list[str]) -> list[str]:
    out: list[str] = []
    n = 1
    for block in blocks:
        lines = block.strip().splitlines()
        if not lines:
            continue
        if lines[0].strip().isdigit() and len(lines) >= 2:
            lines[0] = str(n)
        out.append("\r\n".join(lines))
        n += 1
    return out


def clean_srt_text(text: str) -> tuple[str, int]:
    blocks = re.split(r"\r?\n\r?\n", text.strip())
    kept: list[str] = []
    removed = 0
    for block in blocks:
        if not block.strip():
            continue
        if subtitle_has_promo(block):
            removed += 1
            continue
        kept.append(block)
    return "\r\n\r\n".join(renumber_srt_blocks(kept)) + ("\r\n" if kept else ""), removed


def clean_ass_text(text: str) -> tuple[str, int]:
    out: list[str] = []
    removed = 0
    for line in text.splitlines():
        marker = line.lstrip().lower()
        if (marker.startswith("dialogue:") or marker.startswith("comment:")) and subtitle_has_promo(line):
            removed += 1
            continue
        out.append(line)
    return "\r\n".join(out) + ("\r\n" if out else ""), removed


def clean_vtt_text(text: str) -> tuple[str, int]:
    parts = re.split(r"\r?\n\r?\n", text.strip())
    if not parts:
        return text, 0
    header: list[str] = []
    cues = parts
    if parts[0].lstrip("\ufeff").startswith("WEBVTT"):
        header = [parts[0].strip()]
        cues = parts[1:]

    kept: list[str] = []
    removed = 0
    for cue in cues:
        if subtitle_has_promo(cue):
            removed += 1
            continue
        kept.append(cue.strip())

    output_parts = header + kept
    return "\r\n\r\n".join(output_parts) + ("\r\n" if output_parts else ""), removed


def clean_subtitle_file(input_path: Path, output_path: Path) -> int:
    text = input_path.read_text(encoding="utf-8-sig", errors="replace")
    ext = input_path.suffix.lower()
    if ext == ".srt":
        cleaned, removed = clean_srt_text(text)
    elif ext in {".ass", ".ssa"}:
        cleaned, removed = clean_ass_text(text)
    elif ext == ".vtt":
        cleaned, removed = clean_vtt_text(text)
    else:
        cleaned, removed = text, 0
    output_path.write_text(cleaned, encoding="utf-8", newline="")
    return removed


def yes_no(value: object) -> str:
    return "yes" if bool(value) else "no"


def subtitle_track_options(track: dict) -> list[str]:
    props = track.get("properties") or {}
    opts: list[str] = []
    lang = props.get("language_ietf") or props.get("language") or "und"
    opts += ["--language", f"0:{lang}"]

    name = props.get("track_name")
    if name is not None:
        opts += ["--track-name", f"0:{name}"]

    opts += [
        "--default-track",
        f"0:{yes_no(props.get('default_track'))}",
        "--forced-track",
        f"0:{yes_no(props.get('forced_track'))}",
    ]
    if "flag_hearing_impaired" in props:
        opts += ["--hearing-impaired-flag", f"0:{yes_no(props.get('flag_hearing_impaired'))}"]
    return opts


def clean_subtitle_promos_in_mkv(
    mkv_file: Path,
    mkvmerge: Path | None,
    mkvextract: Path | None,
    dry_run: bool,
) -> SubtitleCleanResult:
    if mkvmerge is None or mkvextract is None:
        return SubtitleCleanResult(False, 0, "limpieza subs omitida (faltan mkvmerge/mkvextract)")
    if dry_run:
        return SubtitleCleanResult(False, 0, "limpieza subs omitida en dry-run")

    info = run_json_cmd(
        [
            str(mkvmerge),
            "--identify",
            "--identification-format",
            "json",
            str(mkv_file),
        ]
    )
    tracks = info.get("tracks") or []
    subtitle_tracks = [t for t in tracks if t.get("type") == "subtitles"]
    text_tracks: list[tuple[dict, str]] = []
    non_text_subtitle_ids: list[str] = []
    for track in subtitle_tracks:
        ext = subtitle_track_extension(track)
        if ext:
            text_tracks.append((track, ext))
        else:
            non_text_subtitle_ids.append(str(track.get("id")))

    if not text_tracks:
        return SubtitleCleanResult(False, 0, "sin subtitulos textuales para limpiar")

    with tempfile.TemporaryDirectory(prefix="subtitle_clean_") as temp_name:
        temp_dir = Path(temp_name)
        extract_args = [str(mkvextract), "tracks", str(mkv_file)]
        extracted: list[tuple[dict, Path, Path]] = []
        for track, ext in text_tracks:
            track_id = track["id"]
            raw_path = temp_dir / f"track_{track_id}{ext}"
            clean_path = temp_dir / f"track_{track_id}.clean{ext}"
            extract_args.append(f"{track_id}:{raw_path}")
            extracted.append((track, raw_path, clean_path))

        code, output = run_cmd(extract_args, dry_run=False)
        if code != 0:
            raise RuntimeError(f"mkvextract fallo ({short_text(output)})")

        removed_total = 0
        for _track, raw_path, clean_path in extracted:
            removed_total += clean_subtitle_file(raw_path, clean_path)

        if removed_total == 0:
            return SubtitleCleanResult(False, 0, "sin autopromociones en subtitulos")

        temp_output = unique_path(mkv_file.with_name(f".{mkv_file.stem}.subtitle-clean.tmp.mkv"))
        cmd = [str(mkvmerge), "-o", str(temp_output)]
        if non_text_subtitle_ids:
            cmd += ["--subtitle-tracks", ",".join(non_text_subtitle_ids)]
        else:
            cmd += ["--no-subtitles"]
        cmd.append(str(mkv_file))

        for track, _raw_path, clean_path in extracted:
            cmd += subtitle_track_options(track)
            cmd.append(str(clean_path))

        code, output = run_cmd(cmd, dry_run=False)
        if code != 0:
            if temp_output.exists():
                temp_output.unlink()
            raise RuntimeError(f"mkvmerge fallo ({short_text(output)})")

        temp_output.replace(mkv_file)
        return SubtitleCleanResult(True, removed_total, f"subs limpiados y remuxeados (bloques={removed_total})")


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


def collect_input_files(input_path: str | None, completado_dir: Path, dry_run: bool) -> tuple[list[Path], bool]:
    if not input_path:
        return sanitize_completado(completado_dir, dry_run), False

    path = Path(input_path).resolve()
    if path.is_file():
        return [path], True
    if path.is_dir():
        return [p for p in path.iterdir() if p.is_file()], True
    print(paint(f"No se encontro la entrada indicada: {path}", "yellow"))
    return [], True


def process_mkv(
    mkv_file: Path,
    mkvpropedit: Path,
    mkvmerge: Path | None,
    mkvextract: Path | None,
    brand: str,
    uploader: str,
    cover_path: Path | None,
    track_metadata_overrides: dict[str, list[dict[str, object]]],
    clean_subtitles: bool,
    dry_run: bool,
) -> ProcessResult:
    clean_note = ""
    if clean_subtitles:
        try:
            clean_result = clean_subtitle_promos_in_mkv(mkv_file, mkvmerge, mkvextract, dry_run)
            if clean_result.message:
                clean_note = clean_result.message
        except Exception as exc:
            return ProcessResult(
                file_name=mkv_file.name,
                ok=False,
                warning=False,
                message=f"error limpiando subs ({short_text(str(exc))})",
            )

    try:
        tracks, attachments_count = parse_mkv_header(mkv_file)
    except Exception as exc:
        return ProcessResult(
            file_name=mkv_file.name,
            ok=False,
            warning=False,
            message=f"error leyendo MKV ({short_text(str(exc))})",
        )

    apply_track_metadata_overrides(tracks, track_metadata_overrides)

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

    if attachments_count > 0:
        cmd += [
            "--delete-attachment",
            "mime-type:image/jpeg",
            "--delete-attachment",
            f"name:www.{brand}.net",
        ]

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
        if track.metadata_overridden:
            language_ietf = canonical_language_code(track.language_ietf or track.language)
            language_legacy = legacy_language_code(language_ietf)
            if language_legacy:
                cmd += ["--set", f"language={language_legacy}"]
            cmd += ["--set", f"language-ietf={language_ietf}"]

    if cover_path and (cover_path.exists() or dry_run):
        cmd += [
            "--attachment-name",
            POSTER_FILE_NAME,
            "--attachment-mime-type",
            "image/jpeg",
            "--add-attachment",
            str(cover_path),
            "--attachment-name",
            SMALL_POSTER_FILE_NAME,
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
    if clean_note:
        msg += f", {clean_note}"
    if warning_count:
        msg += f", advertencias={warning_count}"
    return ProcessResult(
        file_name=mkv_file.name,
        ok=True,
        warning=warning_count > 0 or clean_note.startswith("limpieza subs omitida"),
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


def resolve_filebot_exe(root: Path, filebot_path: str | None) -> Path | None:
    candidates: list[Path] = []
    if filebot_path:
        candidates.append(Path(filebot_path))
    candidates.extend(
        [
            DEFAULT_FILEBOT_EXE,
            root.parent / "2 Rename" / "filebot.exe",
            root / "filebot.exe",
        ]
    )

    path_cmd = shutil.which("filebot")
    if path_cmd:
        candidates.append(Path(path_cmd))

    for candidate in candidates:
        if candidate.exists() and candidate.is_file():
            return candidate
    return None


def launch_filebot_rename(
    root: Path,
    mkv_files: list[Path],
    dry_run: bool,
    filebot_output: Path | None = None,
    filebot_path: str | None = None,
) -> bool:
    filebot_exe = resolve_filebot_exe(root, filebot_path)
    if filebot_exe is None:
        print(paint("FileBot no se lanzo: no se encontro filebot.exe.", "yellow"))
        return False

    output_dir = filebot_output or (mkv_files[0].parent if mkv_files else root)
    output_dir.mkdir(parents=True, exist_ok=True)
    suggested_file = mkv_files[0] if mkv_files else None

    if dry_run:
        print(f"[DRY] Abrir FileBot GUI desde exe: {filebot_exe}")
        print(f"[DRY] Carpeta sugerida para renombrado manual: {output_dir}")
        if suggested_file is not None:
            print(f"[DRY] Archivo sugerido: {suggested_file}")
        return True

    try:
        subprocess.Popen(
            [str(filebot_exe)],
            cwd=str(output_dir if output_dir.exists() else filebot_exe.parent),
            close_fds=True,
        )
    except OSError as exc:
        print(paint(f"No se pudo abrir FileBot desde exe ({short_text(str(exc))})", "red"))
        return False

    print(paint(f"FileBot GUI abierto desde exe para renombrado manual: {filebot_exe}", "cyan"))
    print(f"Carpeta sugerida para renombrado manual: {output_dir}")
    if suggested_file is not None:
        print(f"Archivo sugerido: {suggested_file}")
    return True


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="OK Props autonomo y rapido")
    parser.add_argument("--brand", default="GDriveLatinoHD", help="Marca final.")
    parser.add_argument("--uploader", default="el_inmortus", help="Texto de muxing-application.")
    parser.add_argument("--mkvpropedit", help="Ruta manual de mkvpropedit(.exe).")
    parser.add_argument("--mkvmerge", help="Ruta manual de mkvmerge(.exe).")
    parser.add_argument("--mkvextract", help="Ruta manual de mkvextract(.exe).")
    parser.add_argument("--cover", help="Ruta de imagen JPG para adjunto (opcional).")
    parser.add_argument("--input", help="Archivo o carpeta MKV a etiquetar sin copiar a Completado.")
    parser.add_argument("--track-metadata-overrides", help="JSON de metadata manual de tracks generado por la app.")
    parser.add_argument("--filebot", help="Ruta manual de filebot.exe.")
    parser.add_argument("--filebot-output", help="Carpeta donde FileBot debe dejar los archivos renombrados.")
    parser.add_argument("--no-cover", action="store_true", help="No buscar ni incrustar cover/poster.")
    parser.add_argument("--no-subtitle-clean", action="store_true", help="No limpiar autopromociones dentro de subtitulos textuales.")
    parser.add_argument("--workers", type=int, default=max(1, min(4, os.cpu_count() or 1)))
    parser.add_argument("--dry-run", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    root = Path.cwd()
    dirs = ensure_dirs(root)
    filebot_output = Path(args.filebot_output).resolve() if args.filebot_output else None
    if filebot_output is not None:
        filebot_output.mkdir(parents=True, exist_ok=True)
        print(f"Salida FileBot fijada por workflow: {filebot_output}")

    mkvpropedit = find_mkvpropedit(root, args.mkvpropedit)
    if not mkvpropedit:
        print("No se encontro mkvpropedit.exe.")
        print("Colocalo en `Recursos\\mkvpropedit.exe` o pasa --mkvpropedit <ruta>.")
        return 1
    mkvmerge = find_mkvtool(root, "mkvmerge", args.mkvmerge, mkvpropedit)
    mkvextract = find_mkvtool(root, "mkvextract", args.mkvextract, mkvpropedit)
    clean_subtitles = not args.no_subtitle_clean
    if clean_subtitles and (not mkvmerge or not mkvextract):
        print(paint("Limpieza de subtitulos limitada: faltan mkvmerge/mkvextract; se continuara etiquetando.", "yellow"))
    elif clean_subtitles:
        print("Limpieza de autopromociones en subtitulos activada.")
    else:
        print("Limpieza de autopromociones en subtitulos desactivada.")

    files, explicit_input = collect_input_files(args.input, dirs["completado"], args.dry_run)
    mkv_files = [p for p in files if p.suffix.lower() == ".mkv" and p.exists()]
    if not mkv_files:
        print("-------------------------------------")
        print("--- NO HAY ARCHIVOS PARA TRABAJAR ---")
        print("-------------------------------------")
        renamer_ok = launch_filebot_rename(root, mkv_files, args.dry_run, filebot_output, args.filebot)
        return 0 if renamer_ok else 2

    cover_path = None if args.no_cover else resolve_cover_path(dirs["recursos"], mkv_files, args.cover, args.dry_run)
    if args.no_cover:
        print("Cover/poster desactivado por configuracion.")
    track_metadata_overrides = load_track_metadata_overrides(args.track_metadata_overrides)

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
                mkvmerge,
                mkvextract,
                args.brand,
                args.uploader,
                cover_path,
                track_metadata_overrides,
                clean_subtitles,
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

    moved_v = moved_s = moved_a = 0
    if not explicit_input:
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

    renamer_ok = launch_filebot_rename(root, mkv_files, args.dry_run, filebot_output, args.filebot)

    return 0 if fail_count == 0 and renamer_ok else 2


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("\nCancelado por usuario.")
        raise SystemExit(130)
