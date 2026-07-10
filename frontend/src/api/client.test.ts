import { describe, expect, it } from 'vitest';
import { extractErrorMessage } from './client';

function axiosErrorWithBody(data: unknown, status = 400) {
  return Object.assign(new Error('Request failed'), { isAxiosError: true, response: { data, status } });
}

describe('extractErrorMessage', () => {
  it('joins ValidationProblemDetails field errors, prefixed by field name', () => {
    const err = axiosErrorWithBody({
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { platform: ["The JSON value could not be converted to RealLifeServer.Domain.Enums.StreamPlatform."] }
    });

    const message = extractErrorMessage(err, 'fallback');

    expect(message).toBe('platform: The JSON value could not be converted to RealLifeServer.Domain.Enums.StreamPlatform.');
  });

  it('joins multiple fields and multiple messages per field', () => {
    const err = axiosErrorWithBody({
      errors: {
        rtmpUrl: ['RTMP-URL muss mit rtmp:// oder rtmps:// beginnen'],
        streamKey: ['Required', 'Must not be empty']
      }
    });

    const message = extractErrorMessage(err, 'fallback');

    expect(message).toContain('rtmpUrl: RTMP-URL muss mit rtmp:// oder rtmps:// beginnen');
    expect(message).toContain('streamKey: Required');
    expect(message).toContain('streamKey: Must not be empty');
  });

  it('falls back to title when there is no errors dict', () => {
    const err = axiosErrorWithBody({ title: '"http://x" is not a valid rtmp:// or rtmps:// URL.' });

    expect(extractErrorMessage(err, 'fallback')).toBe('"http://x" is not a valid rtmp:// or rtmps:// URL.');
  });

  it('falls back to the provided fallback when the body has neither errors nor title', () => {
    const err = axiosErrorWithBody({});

    expect(extractErrorMessage(err, 'fallback')).toBe('fallback');
  });

  it('falls back to the provided fallback for a non-axios error', () => {
    expect(extractErrorMessage(new Error('boom'), 'fallback')).toBe('fallback');
  });

  it('only ever reads errors/title - ignores any other field on the body, e.g. an echoed value', () => {
    // Guards against ever accidentally serializing request data (e.g. a submitted stream key)
    // into the displayed message, even if some future response body carried it under an
    // unrelated key - only the errors dict's own message strings are ever used.
    const err = axiosErrorWithBody({
      errors: { streamKey: ['A stream key is required.'] },
      submittedValue: 'super-secret-key'
    });

    const message = extractErrorMessage(err, 'fallback');

    expect(message).toBe('streamKey: A stream key is required.');
    expect(message).not.toContain('super-secret-key');
  });
});
