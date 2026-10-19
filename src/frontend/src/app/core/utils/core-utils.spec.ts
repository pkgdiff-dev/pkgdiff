import { HttpErrorResponse } from '@angular/common/http';

import { apiErrorMessage, loadFailureOf, rejectionMessage } from './http-error.utils';
import { excerptOf, stripMarkdown } from './markdown.utils';

describe('http error helpers', () => {
  it('tells a missing item from a failed request', () => {
    expect(loadFailureOf(new HttpErrorResponse({ status: 404 }))).toBe('not-found');
    expect(loadFailureOf(new HttpErrorResponse({ status: 500 }))).toBe('error');
    expect(loadFailureOf(new HttpErrorResponse({ status: 0 }))).toBe('error');
    expect(loadFailureOf(new Error('boom'))).toBe('error');
  });

  it('takes the message the API sent, in the field it sent it in', () => {
    const failure = (error: unknown) => new HttpErrorResponse({ status: 400, error });

    expect(apiErrorMessage(failure({ error: 'Invalid version format: abc' }), 'fallback')).toBe('Invalid version format: abc');
    expect(apiErrorMessage(failure({ title: 'Bad Request', detail: 'Registry is disabled.' }), 'fallback')).toBe('Registry is disabled.');
    expect(apiErrorMessage(failure({ message: 'Too many requests.' }), 'fallback')).toBe('Too many requests.');
  });

  it('falls back when the response carries no usable message', () => {
    expect(apiErrorMessage(new HttpErrorResponse({ status: 500, error: '<html>502</html>' }), 'fallback')).toBe('fallback');
    expect(apiErrorMessage(new HttpErrorResponse({ status: 500, error: { error: '  ' } }), 'fallback')).toBe('fallback');
    expect(apiErrorMessage(new HttpErrorResponse({ status: 500, error: { error: { nested: true } } }), 'fallback')).toBe('fallback');
    expect(apiErrorMessage(new Error('boom'), 'fallback')).toBe('fallback');
  });

  it('passes on why a request was turned down, and nothing else', () => {
    const body = { error: 'Passwords must be at least 12 characters.' };

    expect(rejectionMessage(new HttpErrorResponse({ status: 400, error: body }), 'fallback')).toBe(body.error);
    expect(rejectionMessage(new HttpErrorResponse({ status: 400 }), 'fallback')).toBe('fallback');
    expect(rejectionMessage(new HttpErrorResponse({ status: 500, error: { error: 'An error occurred' } }), 'fallback')).toBe('fallback');
    expect(rejectionMessage(new Error('boom'), 'fallback')).toBe('fallback');
  });
});

describe('markdown helpers', () => {
  it('reduces Markdown to its text', () => {
    const markdown = '## What changed\n\n- **Added** a [new API](https://example.test)\n- Fixed `parse()`\n\n---\n1. *First*';

    expect(stripMarkdown(markdown)).toBe('What changed Added a new API Fixed First');
  });

  it('cuts an excerpt at a word and marks the cut', () => {
    const text = 'word '.repeat(60);

    const excerpt = excerptOf(text, 50);

    expect(excerpt.length).toBeLessThanOrEqual(50);
    expect(excerpt).toBe('word word word word word word word word word...');
    expect(excerptOf('Short text.', 50)).toBe('Short text.');
  });
});
