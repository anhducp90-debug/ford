# Ford - A Simple Greeting Application

A minimalist Node.js application that says "Hi" to the world or a specific person.

## Installation

1. Clone the repository
2. Navigate to the project directory
3. Run `npm install` (optional, no dependencies required)

## Usage

### Run the application

```bash
# Say hi to the world
npm start
# or
node index.js

# Say hi to a specific person
node index.js Alice
```

### Run tests

```bash
npm test
```

## Features

- Simple greeting functionality
- Customizable name parameter
- Command-line interface
- Basic test suite

## API

### `sayHi(name)`

Returns a greeting string.

- `name` (string, optional): The name to greet. Defaults to "World".
- Returns: A greeting string in the format "Hi, {name}!"

## Examples

```javascript
const { sayHi } = require('./index.js');

console.log(sayHi());        // "Hi, World!"
console.log(sayHi('Alice')); // "Hi, Alice!"
```