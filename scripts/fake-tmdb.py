"""A tiny stand-in for TMDB and OMDb for the real-input tests. No keys, no network.

  python fake-tmdb.py <port>

Also a tiny OpenSubtitles under /os/ (search, download, one .srt file), for the subtitle tests.

Serves /3/search/movie, /3/search/tv, /3/movie/100, /3/tv/200, /3/tv/200/season/1, /omdb/ and /img/<size>/<path> (small coloured PNGs).
Point Lumina at it with LUMINA_TMDB_API=http://127.0.0.1:<port>/3/ LUMINA_TMDB_IMG=http://127.0.0.1:<port>/img/ LUMINA_OMDB_API=http://127.0.0.1:<port>/omdb/
"""
import json
import struct
import sys
import zlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def png(w, h, rgb):
    row = b"\x00" + bytes(rgb) * w
    raw = row * h

    def chunk(kind, data):
        c = struct.pack(">I", len(data)) + kind + data
        return c + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


MOVIE = {
    "runtime": 100, "tagline": "Stay together", "imdb_id": "tt0000100",
    "genres": [{"id": 1, "name": "Science Fiction"}, {"id": 2, "name": "Mystery"}, {"id": 3, "name": "Thriller"}],
    "release_dates": {"results": [{"iso_3166_1": "US", "release_dates": [{"certification": "PG-13"}]}]},
    "images": {"logos": [{"file_path": "/logo100.png", "iso_639_1": "en", "vote_average": 5}]},
    "credits": {
        "crew": [{"name": "David Robert Mitchell", "job": "Director", "profile_path": "/dir.png"}],
        "cast": [{"name": "Anne Hathaway", "character": "Denise Platt", "profile_path": "/anne.png"},
                 {"name": "Ewan McGregor", "character": "Greg Platt", "profile_path": "/ewan.png"},
                 {"name": "Walker Russell", "character": "Mason"}],
    },
}

SHOW = {
    "episode_run_time": [54], "genres": [{"id": 9, "name": "Drama"}],
    "content_ratings": {"results": [{"iso_3166_1": "US", "rating": "TV-14"}]},
    "external_ids": {"imdb_id": "tt0000200"}, "created_by": [{"name": "Terry Matalas", "profile_path": "/terry.png"}],
    "images": {"logos": [{"file_path": "/logo200.png", "iso_639_1": "en"}]},
    "credits": {"cast": [{"name": "Paul Bettany", "character": "Vision", "profile_path": "/paul.png"}]},
}

SEASON = {"episodes": [
    {"episode_number": 1, "name": "No Strings", "overview": "Vision embarks on a journey.", "still_path": "/s1.png", "air_date": "2026-10-13", "runtime": 54, "vote_average": 8.2},
    {"episode_number": 2, "name": "Theseus", "overview": "Vision comes face to face.", "still_path": "/s2.png", "air_date": "2026-10-13", "runtime": 46, "vote_average": 8.2},
]}


SRT = "1\n00:00:01,000 --> 00:00:04,000\nHello from the fake OpenSubtitles\n\n2\n00:00:05,000 --> 00:00:08,000\nSecond line\n"


def hit(file_id, lang, release, downloads, exact=False):
    return {"attributes": {"language": lang, "release": release, "download_count": downloads, "moviehash_match": exact,
                           "files": [{"file_id": file_id}]}}


class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def send(self, code, body, ctype="application/json"):
        data = body if isinstance(body, bytes) else json.dumps(body).encode()
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        length = int(self.headers.get("Content-Length") or 0)
        if length: self.rfile.read(length)
        path = self.path.split("?")[0]
        if path.startswith("/os/download"):
            return self.send(200, {"link": f"http://127.0.0.1:{self.server.server_port}/os/files/dl.srt", "remaining": 99})
        self.send(404, {})

    def do_GET(self):
        path = self.path.split("?")[0]
        if path.startswith("/os/subtitles"):
            return self.send(200, {"data": [hit(1001, "en", "Fake.Release.EN.best", 900), hit(1002, "en", "Fake.Release.EN.other", 20), hit(1003, "es", "Fake.Release.ES", 400)]})
        if path.startswith("/os/files/"):
            return self.send(200, SRT.encode("utf8"), "application/x-subrip")
        if path.startswith("/3/search/movie"):
            return self.send(200, {"results": [{"id": 100, "title": "The End of Oak Street", "overview": "After a mysterious cosmic event rips Oak Street from suburbia, a family discovers their survival depends on sticking together.",
                                               "poster_path": "/poster100.png", "backdrop_path": "/backdrop100.png", "vote_average": 7.1, "release_date": "2026-01-01"}]})
        if path.startswith("/3/search/tv"):
            return self.send(200, {"results": [{"id": 200, "name": "VisionQuest", "overview": "Vision embarks on a journey of self-discovery.",
                                               "poster_path": "/poster200.png", "backdrop_path": "/backdrop200.png", "vote_average": 7.5, "first_air_date": "2026-10-13"}]})
        if path == "/3/movie/100":
            return self.send(200, MOVIE)
        if path == "/3/tv/200":
            return self.send(200, SHOW)
        if path == "/3/tv/200/season/1":
            return self.send(200, SEASON)
        if path.startswith("/omdb/"):
            return self.send(200, {"imdbRating": "6.2", "Ratings": [{"Source": "Internet Movie Database", "Value": "6.2/10"}, {"Source": "Rotten Tomatoes", "Value": "86%"}]})
        if path.startswith("/img/"):
            size = path.split("/")[2]
            w = int("".join(c for c in size if c.isdigit()) or "200")
            h = int(w * 1.5) if "poster" in path or "anne" in path or "ewan" in path or "dir" in path or "paul" in path or "terry" in path else int(w * 0.5625)
            seed = sum(path.encode())
            return self.send(200, png(min(w, 400), min(h, 300), (60 + seed % 150, 60 + (seed * 7) % 150, 60 + (seed * 13) % 150)), "image/png")
        self.send(404, {})


if __name__ == "__main__":
    ThreadingHTTPServer(("127.0.0.1", int(sys.argv[1])), H).serve_forever()
