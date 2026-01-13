const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const sourcePath = path.join(repoRoot, 'CHANGELOG.md');
const destinationPath = path.resolve(__dirname, '..', 'CHANGELOG.md');

if (!fs.existsSync(sourcePath)) {
  throw new Error(`CHANGELOG.md not found at ${sourcePath}`);
}

fs.copyFileSync(sourcePath, destinationPath);
