using System.Security.Cryptography;
using System.Text.Json;

namespace Overlap.Api.Drawing;

public sealed record DrawingCategory(string Id, string Title, string[] Prompts);

public static class DrawingCatalog
{
    public static readonly string[] DefaultCategories = ["animals", "nature", "food", "everyday"];
    public static readonly DrawingCategory[] Categories = [
        new("animals", "Animals", ["A cat", "A dog", "A rabbit", "A fox", "A horse", "A lion", "A tiger", "A bear", "A panda", "A penguin", "An owl", "A duck", "A butterfly", "A bee", "A turtle", "A frog", "A fish", "A dolphin", "An elephant", "A giraffe", "A snail", "A wolf", "A deer", "A squirrel", "A hedgehog", "A raccoon", "A koala", "A kangaroo", "A zebra", "A hippo", "A crocodile", "A snake", "A lizard", "An octopus", "A crab", "A jellyfish", "A seahorse", "A whale", "A shark", "A flamingo", "A peacock", "A parrot", "A swan", "A rooster", "A bat"]),
        new("nature", "Nature", ["A flower", "A rose", "A sunflower", "A tulip", "A cactus", "A tree", "A leaf", "A mushroom", "A mountain", "A waterfall", "A seashell", "A snowflake", "The moon", "A rainbow", "The sun", "A cloud", "A star", "A comet", "A volcano", "A palm tree", "A pine tree", "A fern", "A bamboo stalk", "A dandelion", "A daisy", "A lotus flower", "A cherry blossom", "An acorn", "A pine cone", "A pebble", "A crystal", "A coral branch", "A wave", "A sand dune", "An iceberg", "A lake", "A river", "A lightning bolt", "A planet", "An autumn branch"]),
        new("food", "Food", ["An apple", "A banana", "A strawberry", "A lemon", "A pear", "A pineapple", "A slice of pizza", "An ice cream", "A croissant", "A cupcake", "A bowl of ramen", "A sushi roll", "A watermelon slice", "A bunch of grapes", "A cherry", "A peach", "An avocado", "A coconut", "An orange", "A kiwi slice", "A carrot", "A tomato", "A broccoli floret", "A bell pepper", "A pumpkin", "A potato", "A baguette", "A pretzel", "A doughnut", "A pancake stack", "A waffle", "A fried egg", "A burger", "A taco", "A bowl of soup", "A cheese wedge", "A cookie", "A chocolate bar", "A popcorn bucket", "A birthday cake"]),
        new("everyday", "Everyday things", ["A coffee cup", "A teapot", "A shoe", "A book", "A bicycle", "A house", "A candle", "An umbrella", "A camera", "A guitar", "A backpack", "A pair of glasses", "A key", "A wristwatch", "An alarm clock", "A lamp", "A chair", "A sofa", "A table", "A plant pot", "A watering can", "A broom", "A kettle", "A frying pan", "A pair of scissors", "A pencil", "A paintbrush", "A notebook", "A paperclip", "A stapler", "A toothbrush", "A hairbrush", "A comb", "A sock", "A hat", "A pair of headphones", "A game controller", "A remote control", "A suitcase", "A mailbox"]),
        new("fantasy", "Fantasy", ["A dragon", "A unicorn", "A mermaid", "A fairy", "A wizard", "A castle", "A magic wand", "A treasure chest", "A phoenix", "A robot", "A ghost", "A spaceship", "A goblin", "An elf", "A dwarf", "An ogre", "A troll", "A centaur", "A griffin", "A pegasus", "A kraken", "A sea serpent", "A cyclops", "A sphinx", "A slime monster", "A mimic chest", "A haunted house", "A flying broom", "A potion bottle", "A crystal ball", "A magic lamp", "An enchanted sword", "A knight helmet", "A royal crown", "A pirate ship", "A floating island", "A fairy door", "A mushroom house", "A tiny alien", "A dragon egg"]),
        new("fiction", "Fictional characters", ["Sherlock Holmes", "Harry Potter", "Hermione Granger", "Gandalf", "Frodo", "Darth Vader", "Yoda", "Spider-Man", "Batman", "Wonder Woman", "Sonic", "Mario", "Link", "Lara Croft", "Ron Weasley", "Dobby", "Hagrid", "Legolas", "Gollum", "Aragorn", "Bilbo Baggins", "Luke Skywalker", "Princess Leia", "Chewbacca", "R2-D2", "C-3PO", "The Joker", "Superman", "Iron Man", "Captain America", "Hulk", "Thor", "Black Panther", "Doctor Strange", "Luigi", "Princess Peach", "Bowser", "Kirby", "Zelda", "Winnie the Pooh"]),
        new("anime", "Anime", ["Naruto", "Sasuke (Naruto)", "Kakashi (Naruto)", "Luffy (One Piece)", "Zoro (One Piece)", "Nami (One Piece)", "Goku (Dragon Ball)", "Vegeta (Dragon Ball)", "Sailor Moon", "Totoro", "Chihiro (Spirited Away)", "Howl", "Light (Death Note)", "L (Death Note)", "Edward Elric", "Alphonse Elric", "Tanjiro (Demon Slayer)", "Nezuko (Demon Slayer)", "Sakura (Naruto)", "Hinata (Naruto)", "Itachi (Naruto)", "Gaara (Naruto)", "Sanji (One Piece)", "Chopper (One Piece)", "Robin (One Piece)", "Usopp (One Piece)", "Bulma (Dragon Ball)", "Piccolo (Dragon Ball)", "Gohan (Dragon Ball)", "Frieza (Dragon Ball)", "Kiki (Kiki's Delivery Service)", "Ponyo", "No-Face (Spirited Away)", "Sophie (Howl's Moving Castle)", "Calcifer (Howl's Moving Castle)", "Inosuke (Demon Slayer)", "Zenitsu (Demon Slayer)", "Ash (Pokémon)", "Pikachu (Pokémon)", "Eevee (Pokémon)"]),
        new("snk", "Attack on Titan / SNK", ["Levi (SNK)", "Mikasa (SNK)", "Eren (SNK)", "Armin (SNK)", "Hange (SNK)", "Erwin (SNK)", "Sasha (SNK)", "Jean (SNK)", "Connie (SNK)", "Historia (SNK)", "Reiner (SNK)", "Annie (SNK)", "Pieck (SNK)", "Bertholdt (SNK)", "Zeke (SNK)", "Ymir (SNK)", "Gabi (SNK)", "Falco (SNK)", "Kenny (SNK)", "Marco (SNK)", "Yelena (SNK)", "Onyankopon (SNK)", "Hitch (SNK)", "Petra (SNK)", "Grisha (SNK)", "Carla (SNK)", "Hannes (SNK)", "The Cart Titan (SNK)", "The Colossal Titan (SNK)", "The Armored Titan (SNK)", "The Female Titan (SNK)", "The Beast Titan (SNK)", "The Attack Titan (SNK)", "The Jaw Titan (SNK)", "The War Hammer Titan (SNK)", "The Scout Regiment emblem (SNK)", "The Garrison emblem (SNK)", "The Military Police emblem (SNK)", "The Wings of Freedom (SNK)", "A Scout cloak (SNK)", "A Scout salute (SNK)", "ODM gear (SNK)", "A thunder spear (SNK)", "The walls (SNK)", "The Shiganshina gate (SNK)", "A Survey Corps horse (SNK)", "Levi's cravat (SNK)", "Mikasa's scarf (SNK)", "Armin's book (SNK)", "Annie's ring (SNK)"])
    ];

    public static bool Valid(string[] ids) => ids.Length > 0 && ids.Length <= Categories.Length
        && ids.Distinct().Count() == ids.Length && ids.All(id => Categories.Any(x => x.Id == id));

    public static string[] Selection(DrawingRound round) => round.CategoriesJson is not null
        ? JsonSerializer.Deserialize<string[]>(round.CategoriesJson)!
        : Categories.Where(x => x.Prompts.Contains(round.Prompt)).Select(x => x.Id).DefaultIfEmpty("everyday").ToArray();

    public static string Shuffle(string[] ids, string? previous = null)
    {
        if (!Valid(ids)) throw new ArgumentException("Choose at least one valid drawing category.");
        var prompts = Categories.Where(x => ids.Contains(x.Id)).SelectMany(x => x.Prompts).Where(x => x != previous).ToArray();
        return prompts[RandomNumberGenerator.GetInt32(prompts.Length)];
    }
}
