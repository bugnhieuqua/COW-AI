/**
 * Headless DOM Simulation Harness for COW.AI E2E Tests
 * Provides lightweight mock DOM elements and Duck.ai client chat simulation.
 */

export class MockClassList {
    constructor() {
        this.classes = new Set();
    }
    add(...cls) {
        for (const c of cls) {
            if (c) this.classes.add(c);
        }
    }
    remove(...cls) {
        for (const c of cls) {
            this.classes.delete(c);
        }
    }
    toggle(cls) {
        if (this.classes.has(cls)) {
            this.classes.delete(cls);
            return false;
        } else {
            this.classes.add(cls);
            return true;
        }
    }
    contains(cls) {
        return this.classes.has(cls);
    }
    toString() {
        return Array.from(this.classes).join(' ');
    }
}

export class MockElement {
    constructor(tagName, id = '', className = '') {
        this.tagName = tagName.toUpperCase();
        this.id = id;
        this.classList = new MockClassList();
        if (className) {
            className.split(/\s+/).filter(Boolean).forEach(c => this.classList.add(c));
        }
        this.attributes = new Map();
        this.children = [];
        this.parentElement = null;
        this.style = {};
        this.listeners = {};
        this.value = '';
        this.disabled = false;
        this._innerHTML = '';
        this._textContent = '';
        this.scrollTop = 0;
        this.scrollHeight = 100;
        this.isFocused = false;
        this.onload = null;
    }

    get className() {
        return this.classList.toString();
    }

    set className(val) {
        this.classList = new MockClassList();
        if (val) {
            val.split(/\s+/).filter(Boolean).forEach(c => this.classList.add(c));
        }
    }

    get innerHTML() {
        return this._innerHTML;
    }

    set innerHTML(html) {
        this._innerHTML = html;
        // Strip simple tags for textContent approximation
        this._textContent = html.replace(/<[^>]*>/g, '');
    }

    get textContent() {
        return this._textContent;
    }

    set textContent(text) {
        this._textContent = text;
        // Basic entity encoding for innerHTML representation
        this._innerHTML = String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    setAttribute(name, val) {
        this.attributes.set(name, String(val));
    }

    getAttribute(name) {
        return this.attributes.get(name) || null;
    }

    hasAttribute(name) {
        return this.attributes.has(name);
    }

    addEventListener(event, handler) {
        if (!this.listeners[event]) this.listeners[event] = [];
        this.listeners[event].push(handler);
    }

    removeEventListener(event, handler) {
        if (!this.listeners[event]) return;
        this.listeners[event] = this.listeners[event].filter(h => h !== handler);
    }

    dispatchEvent(event) {
        const handlers = this.listeners[event.type] || [];
        for (const handler of handlers) {
            handler(event);
        }
    }

    appendChild(child) {
        child.parentElement = this;
        this.children.push(child);
        return child;
    }

    removeChild(child) {
        const idx = this.children.indexOf(child);
        if (idx !== -1) {
            child.parentElement = null;
            return this.children.splice(idx, 1)[0];
        }
        return null;
    }

    focus() {
        this.isFocused = true;
    }

    querySelector(selector) {
        const results = this.querySelectorAll(selector);
        return results.length > 0 ? results[0] : null;
    }

    querySelectorAll(selector) {
        const matches = [];
        const isClass = selector.startsWith('.');
        const isId = selector.startsWith('#');
        const isTag = !isClass && !isId;

        const target = selector.slice(1);

        const traverse = (node) => {
            for (const child of node.children) {
                if (isClass && child.classList.contains(target)) {
                    matches.push(child);
                } else if (isId && child.id === target) {
                    matches.push(child);
                } else if (isTag && child.tagName.toLowerCase() === selector.toLowerCase()) {
                    matches.push(child);
                }
                traverse(child);
            }
        };

        traverse(this);
        return matches;
    }
}

export class MockDocument {
    constructor() {
        this.body = new MockElement('body');
        this.documentElement = new MockElement('html');
        this.elementsById = new Map();
        this.listeners = {};
    }

    addEventListener(event, handler) {
        if (!this.listeners[event]) this.listeners[event] = [];
        this.listeners[event].push(handler);
    }

    removeEventListener(event, handler) {
        if (!this.listeners[event]) return;
        this.listeners[event] = this.listeners[event].filter(h => h !== handler);
    }

    dispatchEvent(event) {
        const handlers = this.listeners[event.type] || [];
        for (const handler of handlers) {
            handler(event);
        }
    }

    createElement(tagName) {
        return new MockElement(tagName);
    }

    registerElement(el) {
        if (el.id) {
            this.elementsById.set(el.id, el);
        }
        return el;
    }

    getElementById(id) {
        return this.elementsById.get(id) || null;
    }

    querySelector(selector) {
        if (selector.startsWith('#')) {
            return this.getElementById(selector.slice(1));
        }
        return this.body.querySelector(selector);
    }

    querySelectorAll(selector) {
        return this.body.querySelectorAll(selector);
    }
}

/**
 * Creates a standard Duck.ai DOM harness environment conforming to R1-R4
 */
export function createStandardHarness() {
    const doc = new MockDocument();

    const viewport = doc.registerElement(new MockElement('div', 'chatAppViewport', 'chat-app-viewport'));
    doc.body.appendChild(viewport);

    const heroHeader = doc.registerElement(new MockElement('div', 'chatHeroHeader', 'hero-header'));
    const heroTitle = doc.registerElement(new MockElement('h1', 'heroTitle', 'hero-title'));
    heroHeader.appendChild(heroTitle);
    viewport.appendChild(heroHeader);

    const chatHistory = doc.registerElement(new MockElement('div', 'chatHistory', 'chat-history'));
    viewport.appendChild(chatHistory);

    const suggestionsGroup = doc.registerElement(new MockElement('div', 'chatSuggestionsGroup', 'example-prompts'));
    viewport.appendChild(suggestionsGroup);

    const dockSection = doc.registerElement(new MockElement('div', 'chatDockSection', 'prompt-bar'));
    viewport.appendChild(dockSection);

    const promptForm = doc.registerElement(new MockElement('form', 'chatPromptForm', 'prompt-form'));
    dockSection.appendChild(promptForm);

    const promptInput = doc.registerElement(new MockElement('textarea', 'promptInput', 'prompt-textarea'));
    promptForm.appendChild(promptInput);

    const submitBtn = doc.registerElement(new MockElement('button', 'submitBtn', 'btn-send'));
    promptForm.appendChild(submitBtn);

    const hiddenModel = doc.registerElement(new MockElement('input', 'hiddenSelectedModel'));
    hiddenModel.value = 'gemini';
    promptForm.appendChild(hiddenModel);

    const modelTrigger = doc.registerElement(new MockElement('button', 'modelTrigger', 'model-trigger'));
    const currentModelLabel = doc.registerElement(new MockElement('span', 'currentModelLabel', 'model-trigger__label'));
    currentModelLabel.textContent = 'Gemini 2.0 Flash';
    modelTrigger.appendChild(currentModelLabel);
    dockSection.appendChild(modelTrigger);

    const modelMenu = doc.registerElement(new MockElement('div', 'modelMenu', 'model-menu'));
    dockSection.appendChild(modelMenu);

    const models = [
        { val: 'auto', name: 'Auto (Tự động)' },
        { val: 'gemini', name: 'Gemini 2.0 Flash' },
        { val: 'llama3', name: 'Llama 3 8B' },
        { val: 'deepseek', name: 'DeepSeek R1' },
        { val: 'pollinations', name: 'Pollinations AI' },
        { val: 'omniroute', name: 'OmniRoute' },
        { val: 'duckduckgo', name: 'DuckDuckGo Search' }
    ];

    for (const m of models) {
        const opt = doc.createElement('div');
        opt.className = 'model-option' + (m.val === 'gemini' ? ' selected' : '');
        opt.setAttribute('data-value', m.val);
        const nameSpan = doc.createElement('span');
        nameSpan.className = 'model-option__name';
        nameSpan.textContent = m.name;
        opt.appendChild(nameSpan);
        modelMenu.appendChild(opt);
    }

    return {
        doc,
        viewport,
        heroHeader,
        heroTitle,
        chatHistory,
        suggestionsGroup,
        dockSection,
        promptForm,
        promptInput,
        submitBtn,
        hiddenModel,
        modelTrigger,
        currentModelLabel,
        modelMenu
    };
}
