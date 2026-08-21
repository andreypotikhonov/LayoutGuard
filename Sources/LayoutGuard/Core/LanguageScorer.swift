import Foundation

struct LanguageScorer {
    private let englishWords: Set<String> = [
        "a", "about", "after", "again", "all", "also", "am", "an", "and", "any", "are", "as", "at",
        "back", "be", "because", "been", "before", "but", "by", "can", "come", "could", "day", "did",
        "do", "does", "done", "down", "even", "first", "for", "from", "get", "give", "go", "good", "great",
        "had", "has", "have", "he", "hello", "help", "her", "here", "him", "his", "how", "i", "if", "in",
        "into", "is", "it", "its", "just", "know", "like", "look", "make", "me", "more", "most", "my",
        "need", "new", "no", "not", "now", "of", "on", "one", "only", "or", "other", "our", "out", "over",
        "people", "please", "right", "say", "see", "she", "so", "some", "take", "than", "thank", "that",
        "the", "their", "them", "then", "there", "these", "they", "thing", "think", "this", "time", "to",
        "today", "too", "up", "us", "very", "want", "was", "way", "we", "well", "were", "what", "when",
        "where", "which", "who", "will", "with", "work", "would", "yes", "you", "your"
    ]

    private let russianWords: Set<String> = [
        "а", "без", "был", "была", "были", "быть", "в", "вам", "вас", "ваш", "ведь", "все", "всего",
        "вы", "где", "да", "давай", "даже", "дела", "день", "для", "до", "его", "ее", "если", "есть", "еще", "же",
        "за", "здесь", "и", "из", "или", "им", "их", "как", "когда", "который", "кто", "ли", "мне", "много",
        "может", "можно", "мой", "мы", "на", "надо", "нам", "нас", "наш", "не", "него", "нет", "но", "новый",
        "ну", "о", "об", "один", "она", "они", "оно", "от", "очень", "по", "под", "пожалуйста", "пока",
        "почему", "привет", "при", "про", "работа", "раз", "с", "себя", "сейчас", "сказать", "со", "спасибо",
        "так", "также", "там", "тебе", "тебя", "тем", "теперь", "то", "тогда", "того", "тоже", "только",
        "тот", "тут", "ты", "у", "уже", "хорошо", "хочу", "чего", "чем", "что", "чтобы", "это", "этого",
        "этот", "я"
    ]

    private let englishBigrams: Set<String> = [
        "th", "he", "in", "er", "an", "re", "on", "at", "en", "nd", "ti", "es", "or", "te", "of", "ed",
        "is", "it", "al", "ar", "st", "to", "nt", "ng", "se", "ha", "as", "ou", "io", "le", "ve", "co"
    ]

    private let russianBigrams: Set<String> = [
        "ст", "но", "то", "на", "ен", "ов", "ни", "ра", "во", "ко", "ро", "по", "пр", "ре", "ос", "та",
        "ал", "ли", "от", "го", "ер", "ть", "ет", "ка", "ва", "ит", "не", "ло", "ла", "ес", "ри", "ин"
    ]

    func score(_ word: String, as language: SupportedLanguage) -> Double {
        let normalized = word.lowercased()
        let words = language == .english ? englishWords : russianWords
        let bigrams = language == .english ? englishBigrams : russianBigrams
        var score = words.contains(normalized) ? 9.0 : 0.0

        if LayoutConverter.language(of: normalized) != language {
            score -= 12.0
        }

        let characters = Array(normalized)
        if characters.count > 1 {
            for index in 0..<(characters.count - 1) {
                let pair = String(characters[index...index + 1])
                score += bigrams.contains(pair) ? 0.8 : -0.35
            }
        }

        let vowels = language == .english ? "aeiouy" : "аеёиоуыэюя"
        if normalized.contains(where: { vowels.contains($0) }) {
            score += 0.7
        }

        return score
    }
}
