import Foundation

enum EditDistance {
    static func damerauLevenshtein(_ lhs: String, _ rhs: String) -> Int {
        let left = Array(lhs)
        let right = Array(rhs)
        guard !left.isEmpty else { return right.count }
        guard !right.isEmpty else { return left.count }

        var matrix = Array(
            repeating: Array(repeating: 0, count: right.count + 1),
            count: left.count + 1
        )

        for index in 0...left.count { matrix[index][0] = index }
        for index in 0...right.count { matrix[0][index] = index }

        for row in 1...left.count {
            for column in 1...right.count {
                let substitution = left[row - 1] == right[column - 1] ? 0 : 1
                matrix[row][column] = min(
                    matrix[row - 1][column] + 1,
                    matrix[row][column - 1] + 1,
                    matrix[row - 1][column - 1] + substitution
                )

                if row > 1, column > 1,
                   left[row - 1] == right[column - 2],
                   left[row - 2] == right[column - 1] {
                    matrix[row][column] = min(matrix[row][column], matrix[row - 2][column - 2] + 1)
                }
            }
        }

        return matrix[left.count][right.count]
    }
}
