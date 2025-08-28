#!/usr/bin/env node

/**
 * Simple greeting application
 * Outputs a friendly "Hi" message
 */

function sayHi(name = 'World') {
    return `Hi, ${name}!`;
}

// Main execution
function main() {
    // Get name from command line arguments or use default
    const args = process.argv.slice(2);
    const name = args.length > 0 ? args[0] : 'World';
    
    console.log(sayHi(name));
}

// Export for testing purposes
module.exports = { sayHi };

// Run main function if this file is executed directly
if (require.main === module) {
    main();
}