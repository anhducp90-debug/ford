/**
 * Simple test for the greeting application
 */

const { sayHi } = require('./index.js');

function test(description, fn) {
    try {
        fn();
        console.log(`✓ ${description}`);
    } catch (error) {
        console.log(`✗ ${description}: ${error.message}`);
        process.exit(1);
    }
}

function assertEqual(actual, expected, message) {
    if (actual !== expected) {
        throw new Error(`${message} - Expected: ${expected}, Actual: ${actual}`);
    }
}

// Test cases
test('sayHi() with default parameter should return "Hi, World!"', () => {
    const result = sayHi();
    assertEqual(result, 'Hi, World!', 'Default greeting');
});

test('sayHi("Alice") should return "Hi, Alice!"', () => {
    const result = sayHi('Alice');
    assertEqual(result, 'Hi, Alice!', 'Custom name greeting');
});

test('sayHi("") should return "Hi, !"', () => {
    const result = sayHi('');
    assertEqual(result, 'Hi, !', 'Empty string greeting');
});

console.log('All tests passed! 🎉');