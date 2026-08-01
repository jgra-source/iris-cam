# Third-party notices

## Microsoft VirtualCamera sample

Most of the code in this folder is derived from the **VirtualCamera** sample in
Microsoft's [Windows-Camera](https://github.com/microsoft/Windows-Camera)
repository, used under the MIT licence reproduced below.

### What we changed

- Replaced the source of video. The sample generates a synthetic pattern; this
  version reads frames published by the Iris Camera desktop app through
  shared memory (`SharedFrameReader.h`), and falls back to the sample's pattern
  whenever no frame is available.
- Changed the advertised resolution from 640×480 to 1280×720 to match the
  frames the desktop app produces.
- Changed the component identifier (CLSID) so this camera is distinct from the
  sample and the two can coexist.

Everything else — the Media Foundation media source and stream, activation,
and registration plumbing — is Microsoft's, kept deliberately close to the
original because it was verified working before it was modified.

### Licence

```
MIT License

Copyright (c) Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
