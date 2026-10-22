#!/usr/bin/env python3
"""Write the standard deb ar archive without installing dpkg on the build host."""
import io
import os
from pathlib import Path
import sys
import tarfile

stage, output = map(Path, sys.argv[1:])
epoch = int(os.environ.get('SOURCE_DATE_EPOCH', '0'))

def archive(control):
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode='w:xz', format=tarfile.GNU_FORMAT) as tar:
        paths = sorted((stage / 'DEBIAN').rglob('*')) if control else sorted(stage.rglob('*'))
        for path in paths:
            relative = path.relative_to(stage / 'DEBIAN' if control else stage)
            if not control and relative.parts[0] == 'DEBIAN':
                continue
            entry = tar.gettarinfo(str(path), './' + str(relative))
            entry.uid = entry.gid = 0
            entry.uname = entry.gname = 'root'
            entry.mtime = epoch
            if entry.isfile():
                with path.open('rb') as content:
                    tar.addfile(entry, content)
            else:
                tar.addfile(entry)
    return buffer.getvalue()

with output.open('wb') as deb:
    deb.write(b'!<arch>\n')
    for name, content in [('debian-binary', b'2.0\n'), ('control.tar.xz', archive(True)), ('data.tar.xz', archive(False))]:
        header = f'{name + "/":<16}{epoch:<12}{0:<6}{0:<6}{"100644":<8}{len(content):<10}`\n'
        deb.write(header.encode('ascii'))
        deb.write(content)
        if len(content) % 2:
            deb.write(b'\n')
print(output)
