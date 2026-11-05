/**
 * SSE Parsing, Generation, and Intent Dispatch Helpers for COW.AI E2E Tests
 */

/**
 * Reconstructs data stream from raw SSE chunks using the line buffer algorithm
 */
export function parseSSEChunks(chunks) {
    let buffer = '';
    const events = [];
    let completed = false;

    for (const chunk of chunks) {
        buffer += chunk;
        const lines = buffer.split('\n');
        buffer = lines.pop() || '';

        for (let line of lines) {
            line = line.trim();
            if (!line || !line.startsWith('data:')) continue;
            const data = line.replace(/^data:\s*/, '').trim();

            if (data === '[DONE]') {
                completed = true;
                events.push({ type: 'done', raw: data });
                break;
            }

            try {
                const json = JSON.parse(data);
                events.push({ type: 'json', data: json, raw: data });
            } catch {
                events.push({ type: 'text', data, raw: data });
            }
        }
    }

    return { events, completed, remainderBuffer: buffer };
}

/**
 * Creates standardized OpenRouter-compatible SSE stream chunks for text
 */
export function createMockTextStream(tokens) {
    const chunks = [];
    for (const token of tokens) {
        const payload = JSON.stringify({
            choices: [{
                delta: { content: token }
            }]
        });
        chunks.push(`data: ${payload}\n\n`);
    }
    chunks.push('data: [DONE]\n\n');
    return chunks;
}

/**
 * Creates standardized Pollinations-compatible SSE stream chunks for image
 */
export function createMockImageStream(imageUrl) {
    const payload = JSON.stringify({
        is_image_url: imageUrl,
        choices: [{
            delta: { content: `![image](${imageUrl})\n\n` }
        }]
    });
    return [
        `data: ${payload}\n\n`,
        'data: [DONE]\n\n'
    ];
}

/**
 * Fragments string chunks into arbitrary small slices (e.g. 5 bytes each)
 * to rigorously verify stream buffer resilience across packet boundaries.
 */
export function fragmentChunks(chunks, sliceSize = 7) {
    const fullText = chunks.join('');
    const fragments = [];
    for (let i = 0; i < fullText.length; i += sliceSize) {
        fragments.push(fullText.slice(i, i + sliceSize));
    }
    return fragments;
}

/**
 * Implements the authoritative Intent Detection logic specified in PROJECT.md F8 & M2
 */
export function detectIntent(prompt, model = '') {
    if (!prompt) return 'text';
    const trimmed = prompt.trim();
    const lower = trimmed.toLowerCase();

    if (model && model.toLowerCase() === 'pollinations') {
        return 'image';
    }

    if (lower.startsWith('/image ') || lower.startsWith('/draw ') || lower === '/image' || lower === '/draw') {
        return 'image';
    }

    if (lower.startsWith('vẽ ') || lower.startsWith('tạo ảnh ') || lower.includes('tạo ảnh') || lower.includes('vẽ tranh')) {
        return 'image';
    }

    return 'text';
}

/**
 * Strips slash commands to yield clean prompt
 */
export function cleanPrompt(prompt) {
    if (!prompt) return '';
    const trimmed = prompt.trim();
    if (trimmed.toLowerCase().startsWith('/image ')) {
        return trimmed.slice(7).trim();
    }
    if (trimmed.toLowerCase().startsWith('/draw ')) {
        return trimmed.slice(6).trim();
    }
    return trimmed;
}
