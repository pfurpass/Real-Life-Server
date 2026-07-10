import '@testing-library/jest-dom/vitest';

// jsdom has no real media pipeline - HTMLMediaElement.play()/load() throw "Not implemented" and
// canPlayType() only returns useful values here because we stub it; LivePreviewPlayer calls all
// three on the underlying <video>, so tests need harmless stand-ins instead of jsdom's defaults.
HTMLMediaElement.prototype.play = () => Promise.resolve();
HTMLMediaElement.prototype.load = () => {};
HTMLMediaElement.prototype.canPlayType = () => '';
